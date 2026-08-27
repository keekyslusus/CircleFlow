# Circle to Search — Flow Launcher plugin plan

Circle-to-search for Windows, implemented as a Flow Launcher plugin: hotkey → frozen-screen
overlay → free-form lasso → the cropped region is uploaded to Google Lens and the results page
opens in the default browser.

Deliberately lightweight: the plugin lives inside `Flow.Launcher.exe`, so it keeps no resident UI,
no WebView2, and no background workers. The only state between invocations is the registered
hotkey. Results render in the user's browser, not in the plugin (decision 2026-08, replacing the
standalone-app plan this file supersedes).

## Reference material (read these before coding)

- **GrepFlow — `C:\Users\pooky\Desktop\vm\GrepFlow\`** — same author's working Flow plugin, same
  SDK version and conventions. Mirror it file-by-file:
  - `GrepFlow.csproj` → project file shape: `net9.0-windows`, `UseWPF`, `OutputType=Library`,
    `AppendTargetFrameworkToOutputPath=false`, `AppendRuntimeIdentifierToOutputPath=false`,
    `CopyLocalLockFileAssemblies=true`, `PackageReference Flow.Launcher.Plugin 5.3.1`,
    `Content` includes for `plugin.json` and `Images\*.png`. Rename assembly/namespace to
    `CircleToSearch`; keep `<Compile Remove="tests\**\*.cs" />`.
  - `GrepFlow\Main.cs` → the adapter shape: `Main : IAsyncPlugin, ISettingProvider, IDisposable`,
    null-guarded `QueryAsync`, everything delegated to the runtime built by `CompositionRoot`.
  - `GrepFlow\GrepFlow\CompositionRoot.cs` → composition style and the `PluginRuntime` wrapper with
    the logged `Dispose` (log line so "Reload Plugin Data" leaks are visible — copy this idea).
  - `GrepFlow\GrepFlow\PluginLog.cs` → copy nearly as-is: `plugin.log` + `plugin.log.old`,
    256 KB rotation, UTF-8 no BOM, `Info/Warn/Error(source, message)`.
  - `GrepFlow\GrepFlow\Interop\StaDispatcher.cs` → dedicated STA thread with a running
    `Dispatcher` pump; the hotkey window and the overlay each need exactly this.
  - `GrepFlow\plugin.json` → schema; `ExecuteFileName` is the assembly dll, `IcoPath` a bundled png.
  - `tests\GrepFlow.Tests\GrepFlow.Tests.csproj` → xunit 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1,
    `xunit.runner.visualstudio` 3.1.4, `ProjectReference` to the main csproj.
  - `build_release.ps1` → release/publish script shape.
- **Browser extension (for the record)** —
  `C:\Users\pooky\AppData\Local\imput\Helium\User Data\Profile 3\Extensions\okkmmheplgoglaiofliglhekamdfepmc\2.0.2_0\`.
  It uploads by opening `lens.google.com` and simulating drag-and-drop events from a content
  script (`content.js`). That technique requires code inside the page; we do not use it. Its value
  here is confirming the target page and the "open tab, feed image" pattern.
- **This repo's `AGENTS.md`** — binding: thin `Main.cs`, one composition root, composition +
  constructor injection, no implementation inheritance / base classes / service locators, no XML
  docs, comments only for non-obvious why.

## Verified foundations

Things confirmed before this plan, so they are constraints, not guesses:

- `POST https://www.google.com/searchbyimage/upload` with multipart field `encoded_image`
  (PNG bytes, no cookies/auth, no special User-Agent) answers `302` whose `Location` is a ready
  Lens results URL (`…&udm=26&vsrid=…`; verified 2026-08-27, ~0.8 s cold). The URL renders
  anonymously in a browser. Verified with:
  `curl -F "encoded_image=@t.png;type=image/png" https://www.google.com/searchbyimage/upload`
- `Flow.Launcher.Plugin` 5.3.1 (NuGet, lib path `net9.0-windows7.0`) has **no**
  hotkey-registration API. `IPublicAPI.HideMainWindow()` exists; `RegisterGlobalHotkey` does not.
  `RegisterGlobalKeyboardCallback` exists but its semantics (fires when Flow is hidden?) are
  unverified; the plan uses Win32 `RegisterHotKey` instead.
- Useful `IPublicAPI` members (see GrepFlow usage): `HideMainWindow()`, `ShowMsgError(title, msg)`,
  `LoadSettingJsonStorage<T>()` / `SaveSettingJsonStorage<T>()`,
  `context.CurrentPluginMetadata.PluginDirectory` (log file + Images).

## UX flow

1. Global hotkey (default `Ctrl+Alt+Space`, configurable) or Flow query `cs` + Enter.
2. Flow's main window is hidden; the monitor under the pointer is captured once and shown as a
   frozen fullscreen topmost overlay on that monitor.
3. User draws a free-form lasso. `Esc`, right-click, backdrop click, or losing activation cancels.
   Repeated hotkey while the overlay is open cancels (deviation from the old plan's "recapture"
   default — simpler and predictable). Hotkey during upload is ignored (upload is a ~1 s window;
   queueing a second browser redirect helps nobody).
4. On release: bounding rectangle of the lasso (+ small padding, clamped to the monitor) is cropped
   from the frozen frame, downscaled if above the long-side cap, encoded as PNG in memory.
5. The active provider uploads the PNG and returns a results URL; the plugin opens it in the
   default browser and disposes the overlay and bitmaps. Nothing is written to disk; the clipboard
   is untouched.

## Repository layout

```
plugin.json                      # content below
Images/app.png                   # icon, IcoPath; any placeholder 128x128 for MVP
CircleToSearch.csproj            # mirror GrepFlow.csproj, renamed
Main.cs                          # IAsyncPlugin, ISettingProvider, IDisposable — adapter only
CTS/CompositionRoot.cs           # assembles the graph; PluginRuntime with logged Dispose
CTS/Interop/NativeMethods.cs     # P/Invoke: RegisterHotKey/UnregisterHotKey, CreateWindowExW,
                                 #   SetThreadDpiAwarenessContext, GetDpiForMonitor,
                                 #   EnumDisplayMonitors/MonitorFromPoint
CTS/Interop/HotkeyWindow.cs      # message-only window + WM_HOTKEY pump on a dedicated STA thread
CTS/Trigger/HotkeyRegistrar.cs   # parses the configured gesture, registers, reports conflicts
CTS/Trigger/QueryTrigger.cs      # "cs" → same StartSelectionAsync entry point
CTS/Capture/OverlayWindow.cs     # frozen frame + lasso path; Esc/backdrop/right-click cancel
CTS/Capture/LassoBoundsCalculator.cs
CTS/Capture/ImageCropper.cs      # crop + long-side downscale + PNG encode, all in memory
CTS/Search/IVisualSearchProvider.cs
CTS/Search/GoogleLensProvider.cs # POST + 302 handling via HttpClient
CTS/Search/RedirectUrlPolicy.cs  # https + google.com suffix-host validation of the Location
CTS/Search/SearchCoordinator.cs  # idle → selecting → uploading → open; cancellation
CTS/Settings/PluginSettings.cs
CTS/Settings/SettingsPanel.cs
tests/CircleToSearch.Tests/      # xunit, mirror GrepFlow.Tests csproj
```

`plugin.json` (ID pregenerated, do not change after first install):

```json
{
  "ID": "32d4962d-b408-4f35-9246-6f4bce5aa8fd",
  "ActionKeyword": "cs",
  "Name": "Circle to Search",
  "Description": "Select a screen area and search it with Google Lens",
  "Author": "keekys",
  "Version": "0.1.0",
  "Language": "csharp",
  "ExecuteFileName": "CircleToSearch.dll",
  "IcoPath": "Images\\app.png"
}
```

## Component contracts

**Main.cs.** `Task InitAsync(PluginInitContext context)` builds `ITextProvider`-free minimal graph:
`CompositionRoot.Create(context)` → `PluginRuntime`. `QueryAsync` returns one result
("Select screen area…", `Action` = `StartSelectionAsync`) for any non-empty query; context menus:
none for MVP. `Dispose` → `_runtime?.Dispose()`.

**HotkeyRegistrar / HotkeyWindow.** Message-only window (`CreateWindowExW` with `HWND_MESSAGE`
parent) on a dedicated STA thread with a `Dispatcher` pump (StaDispatcher pattern). Register with
`RegisterHotKey(hwnd, 1, modifiers, vk)`; pump `WM_HOTKEY` (0x0312) → callback. Modifiers:
`MOD_ALT=0x1, MOD_CONTROL=0x2, MOD_SHIFT=0x4, MOD_WIN=0x8`; default gesture `Ctrl+Alt+Space`
(VK_SPACE = 0x20). Settings store the gesture as `"Ctrl+Alt+Space"`; parser tests cover the
round trip. Registration failure → log + status surfaced in settings panel and via a `cs` result;
never throws across the boundary. `Dispose` → `UnregisterHotKey` + window/thread teardown.

**Capture & DPI — the main spike.** The plugin inherits Flow's process DPI awareness and cannot
change it, so: on the overlay thread call
`SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` (handle `(void*)-4`),
keep the returned previous context, restore it on teardown. With PMv2 active on that thread,
monitor bounds from `EnumDisplayMonitors` are physical pixels; capture the pointer's monitor with
`Graphics.CopyFromScreen` into a `Bitmap`. Lasso coordinates, crop, and the PNG stay physical
pixels end-to-end. WPF sizes windows in DIPs: convert monitor physical rect → DIP by
`GetDpiForMonitor` scale (e.g. 1.5× at 150 %) for the overlay's Left/Top/Width/Height — this
conversion is exactly what the spike must validate on a mixed-DPI dual-monitor setup.

**OverlayWindow.** Created per invocation on its own STA thread (no `Application` object;
`Dispatcher.Run`), `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=False`,
`Topmost=True`, `ShowActivated=True`, bounds = monitor rect (in DIPs), `Background` =
frozen-frame image, `Canvas` + `Polyline` for the lasso path (target 60 Hz; invalidate on
mouse move, no per-point layout churn). Cancel paths: `Esc` key, right-click, backdrop click
(mouse-down without move + release inside), `Deactivated` event, coordinator cancel.
On confirm: raise `Completed(physicalBounds)` and shut the window down; dispose the frozen
frame after the crop is taken (the cropper copies pixels out first).

**LassoBoundsCalculator.** Pure static: `Point[]` path + monitor rect → bounding rect with
`Padding` (8 physical px), clamped to monitor; rejects gestures below a minimum area/diagonal
(e.g. < 12 px diagonal) as `null` → treated as cancel.

**ImageCropper.** Pure: source bitmap + rect → crop (`Clone`), downscale only if long side
exceeds `MaxLongSidePx` (default 1600, settings), high-quality bicubic, encode PNG to `byte[]`.
No temp files.

**GoogleLensProvider.** One `HttpClient` (static/lazy): `POST
https://www.google.com/searchbyimage/upload`, `MultipartFormDataContent` with
`ByteArrayContent` named `encoded_image`, filename `capture.png`, `ContentType = image/png`;
`Timeout = 10 s`; `AllowAutoRedirect = false`. Success = status 302 with non-empty `Location`
passing `RedirectUrlPolicy` → return the URL string. Everything else (200 consent page, 429,
timeout, HttpRequestException, policy rejection) → typed failure (reason enum + status code),
no exception escapes.

**RedirectUrlPolicy.** `Uri` must be `https`; host label-suffix match `google.com` (so
`www.google.com`, `lens.google.com` pass; `google.com.evil.test`, `notgoogle.com` fail).

**SearchCoordinator.** Owns a `SemaphoreSlim(1,1)` session. States: idle → selecting (overlay
live) → uploading → open (Process.Start) → idle. `ProcessStartInfo { UseShellExecute = true,
FileName = url }` — default browser; failures here → `ShowMsgError`. All public methods
exception-safe: catch, log, surface, return to idle. Entry points: hotkey callback, query action.

**Settings.** `PluginSettings`: `HotkeyGesture` ("Ctrl+Alt+Space"), `MaxLongSidePx` (1600),
`PaddingPx` (8), `HideDelayMilliseconds` (60), `LassoMinDiagonalPx` (12). Load via
`api.LoadSettingJsonStorage<PluginSettings>()`, save on panel apply via
`api.SaveSettingJsonStorage<PluginSettings>()`, then re-register the hotkey. Panel is a plain
WPF `UserControl` (GrepFlow `SettingsPanel` pattern); MVP controls: gesture text + max-size.

**Crash containment.** Shared process with Flow: no exception may escape plugin entry points or
any of our threads; every thread boundary try/catches, logs via `PluginLog`, and degrades to
idle. `PluginRuntime.Dispose` closes a live overlay, unregisters the hotkey, stops threads, and
writes the "disposing" log line (GrepFlow pattern) — Flow's "Reload Plugin Data" can happen
mid-selection and must not leak a window or thread.

## Testing

Deterministic unit tests (target ≥ 20):

- `LassoBoundsCalculatorTests` — normal lasso; open path auto-bounded; tiny/invalid gesture
  rejected; edge/corner clamping; full-monitor selection; padding rule.
- `ImageCropperTests` — exact pixels from a deterministic colored bitmap; crop at each edge;
  downscale only above the long-side cap; valid PNG signature and dimensions.
- `RedirectUrlPolicyTests` — allow Lens/Google HTTPS; reject HTTP, lookalike and non-Google hosts,
  hostile suffixes (`google.com.evil.test`).
- `LensUploadClientTests` — fake `HttpMessageHandler`: 302 → URL; 200/429/timeout → mapped
  failures; multipart field name `encoded_image`, filename and content type asserted; no exception
  escapes.
- `SearchCoordinatorTests` — idle → selecting → uploading → open; cancel at each state; repeated
  hotkey during selection; hotkey during upload ignored; failure surfaces without escaping.
- `HotkeyGestureParserTests` — settings string ↔ modifier/key round trip; invalid strings.

WPF overlay itself is not unit-tested — covered by the manual matrix.

Opt-in live smoke test (network, `[Trait("Category","Live")]`, excluded from default runs): a
small checked-in, license-safe fixture image must produce a `302` with an https google.com
`Location`. No assertions on result content, ranking, or timing.

Manual acceptance matrix:

1. From a non-browser app: hotkey → lasso an object → default browser shows relevant Lens results.
2. Over a Chromium browser (incl. Helium): pixels-only capture, no extension involved.
3. Sizes: tiny object, large object, text-heavy image, selection touching each edge, near-fullscreen.
4. Scaling 100/125/150/200 % on the primary display; second monitor left/right with different scale
   factors and negative coordinates, mixed DPI — crops must align with what was circled.
5. Lifecycle: cancel before/during drawing, repeated hotkey, 10 consecutive searches,
   "Reload Plugin Data" with the overlay open, Flow restart.
6. Failure paths: offline at start, connection lost during upload — visible error, no hang, no
   crash of Flow.
7. Content limits: UAC/protected windows capture as black/blank — documented, not a crash.
8. Hygiene: no captured image on disk, clipboard unchanged, no image/base64 in logs.

## Performance targets

Measured with release-build timestamps on the dev machine; network-bound result time is reported,
not gated.

- Hotkey → frozen overlay visible: ≤ 100 ms median, ≤ 180 ms p95 (20 runs). The overlay thread and
  window are created per invocation — if creation shows up in the profile, keep a warm hidden
  thread (still zero resident UI) before optimizing anything else.
- Pointer release → upload dispatched: ≤ 50 ms (in-process, no UI in the path).
- Pointer release → results URL handed to the browser: ≤ 1.5 s median on a normal connection
  (upload POST observed ~0.3–0.9 s; report actuals in README).
- Overlay drawing responsive at 60 Hz.
- After 10 completed searches, memory returns to within 20 % of the idle baseline after GC;
  retained bitmaps or the overlay window are the suspects if not.
- Idle resident cost: no windows, no timers, one message-only window and its thread.

## Build, install, verify

- `dotnet build` at repo root (mirrors GrepFlow's layout); `dotnet test` for the suite; release via
  a `build_release.ps1` modeled on `GrepFlow\build_release.ps1` (`dotnet publish -c Release`).
- Dev install: copy build output into Flow's user plugins directory
  (`%APPDATA%\FlowLauncher\Plugins\CircleToSearch\`), then Flow settings → "Reload Plugin Data".
  The plugin must appear under action keyword `cs`; logs land in `plugin.log` next to the dll
  (PluginLog pattern).

## Execution order

1. **Spikes** (throwaway code + log lines, may live in the plugin behind debug entry points):
   (a) DPI — PMv2 thread capture on a mixed-DPI dual-monitor setup, verify physical-pixel crop
   alignment; (b) hotkey — `RegisterHotKey` from the plugin while Flow is hidden, fires,
   unregisters on dispose, conflict path works; (c) run the live smoke manually from the target
   network — a consent-page 200 here activates plan B (open `lens.google.com`, PNG to clipboard,
   Ctrl+V — designed, not implemented unless needed).
2. **Vertical slice**: hotkey → overlay → lasso → crop → hardcoded Lens upload → default browser.
   Proves the full path with zero abstraction.
3. **Componentize** per the layout above, add settings, error paths, `cs` query trigger.
4. **Tests** to ≥ 20 green; **live smoke** + manual matrix; record performance actuals in README.

## Acceptance criteria

- Fresh shell `dotnet build` per documented commands; the plugin loads in Flow with action keyword
  `cs` and a working settings panel.
- End-to-end: hotkey → overlay → lasso → default browser opens Lens results for the cropped region,
  logged out, anonymous.
- No offset crops on mixed-DPI multi-monitor setups (post-spike).
- `Esc`, backdrop click, right-click, repeated hotkey, and Flow reload cleanly dispose the active
  session; dispose is logged.
- No image written to disk, clipboard untouched, no image bytes logged.
- Upload failure produces a visible, recoverable error — Flow itself never crashes or hangs.
- Unit suite ≥ 20 deterministic tests green; live smoke green when run.
- Original scenario, inverse/cancel cases, and the manual matrix pass after the final change.

## Assumptions and post-MVP

- Windows 11 x64 26100, current Flow Launcher, plugin API 5.3.1, `net9.0-windows`.
- Rectangular crop around the lasso is acceptable (unchanged from the old plan).
- Browser redirect replaces in-plugin results; the old plan's WebView2, Lens bridge, tray, and
  "no external browser" requirements are dropped intentionally.
- Post-MVP backlog: additional providers (Bing, Yandex), OCR text mode, recent-search history,
  whole-screen translate mode — each a separate plan; none block the MVP.
