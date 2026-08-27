# Circle to Search (Flow Launcher plugin)

Circle-to-search for Windows: a global hotkey (default `Ctrl+Alt+Space`) or the Flow query `cs`
freezes the screen, you draw a free-form lasso around a region, and the cropped area is uploaded
to **Yandex Images** — the results page opens in your default browser. Anonymous, no account.

The plugin lives inside `Flow.Launcher.exe`: no resident UI, no background workers, no embedded
browser engines. The only persistent state between invocations is the registered hotkey. Nothing
is written to disk; the clipboard is untouched.

## How it works

1. `POST https://yandex.ru/images-apphost/image-download?cbird=111&images_avatars_size=preview&images_avatars_namespace=images-cbir`
   with the **raw PNG bytes** as the request body (this is the same endpoint the Yandex Images
   web UI uses for file uploads; discovered via a headless-browser network capture).
2. The JSON answer carries `cbir_id`; the plugin builds
   `https://yandex.ru/images/search?rpt=imageview&url=<avatars path>&cbir_id=<id>` and opens it
   in the default browser.
3. No API key, no cookies, no session tricks; works from a plain `HttpClient` (unlike Google
   Lens, whose anonymous endpoints are client-fingerprinted — the full investigation lives in
   git history at the `checkpoint: Google Lens` commit).

Measured end-to-end (vb_logo.png, 2026-08-28): upload + results URL ≈ 300–400 ms; the rest is
the results page rendering in your browser.

- Cancel: `Esc`, right-click, a plain click without dragging, losing window activation, or
  pressing the hotkey again while the overlay is open.
- The hotkey is ignored during the ~0.5 s upload window.
- Crops are physical pixels end-to-end; the overlay thread runs
  `SetThreadDpiAwarenessContext(PMv2)` so mixed-DPI multi-monitor setups capture the exact
  region shown (see the manual matrix in PLAN.md).
- UAC/protected windows capture as black/blank — a Windows limitation, not a crash.

## Build

```
dotnet build CircleToSearch.csproj
```

Release build (output in `bin\Release\`):

```
powershell -File build_release.ps1 -NoPause
```

## Tests

```
dotnet test tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj
```

The suite is deterministic and offline (52 tests). The live upload smoke test against the real
Yandex endpoint is opt-in:

```
dotnet test tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj --filter "Category=Live" -e CTS_LIVE=1
```

## Install (dev)

1. Copy the contents of `bin\Release\` into `%APPDATA%\FlowLauncher\Plugins\CircleToSearch\`.
2. Flow Launcher settings → "Reload Plugin Data".
3. The plugin appears under the action keyword `cs`.

Logs land in `plugin.log` (plus `plugin.log.old`) next to the dll.

## Settings

Flow Launcher → plugin settings:

- **Hotkey** — gesture string like `Ctrl+Alt+Space` (canonical order `Win+Ctrl+Alt+Shift`).
  Letters `A–Z`, digits `0–9`, `F1–F12`, `Space`, `Insert/Delete/Home/End/PageUp/PageDown`.
  If the combination is taken by another program this is shown in the panel and in the `cs`
  result subtitle; the plugin keeps working via the `cs` query.
- **Max image long side (px)** — the capture is downscaled only above this (default 1600).

Fixed via the settings file (not exposed in the panel): `PaddingPx` (8), `HideDelayMilliseconds`
(60), `LassoMinDiagonalPx` (12).

## Manual acceptance matrix (still to run on the dev machine)

1. Hotkey → lasso → relevant Yandex results in the default browser, from a non-browser app.
2. Over a browser: pixels-only capture.
3. Sizes: tiny object, large object, text-heavy image, selections touching each edge,
   near-fullscreen.
4. Scaling 100/125/150/200 % on the primary display; second monitor left/right with different
   scale factors and negative coordinates — crops must align with what was circled.
5. Lifecycle: cancel before/during drawing, repeated hotkey, 10 consecutive searches,
   "Reload Plugin Data" with the overlay open, Flow restart (`plugin.log` must show the
   `disposing:` line on every reload — no leaked hotkey or STA thread).
6. Failure paths: offline at start, connection lost during upload — visible error, no hang.
7. Hygiene: no captured image on disk, clipboard unchanged, no image bytes in logs.
