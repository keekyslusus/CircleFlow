# Circle to Search (Flow Launcher plugin)

Circle-to-search for Windows: a global hotkey (default `Ctrl+Alt+Space`) or the Flow query `cs`
freezes the screen, you draw a free-form lasso around a region, and the cropped area is uploaded
to Google Lens — the results page opens in your default browser. Anonymous, logged out.

The plugin lives inside `Flow.Launcher.exe`: no resident UI, no WebView2, no background workers.
The only persistent state between invocations is the registered hotkey. Nothing is written to
disk; the clipboard is untouched.

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

The suite is deterministic and offline (53 tests). The live upload smoke test against the real
Google endpoint is opt-in:

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

- **Search method** — "Paste into Google Lens" (default, works) or "Direct upload" (legacy;
  Google accepts the POST but currently never processes the image).
- **Hotkey** — gesture string like `Ctrl+Alt+Space` (canonical order `Win+Ctrl+Alt+Shift`).
  Letters `A–Z`, digits `0–9`, `F1–F12`, `Space`, `Insert/Delete/Home/End/PageUp/PageDown`.
  If the combination is taken by another program this is shown in the panel and in the `cs`
  result subtitle; the plugin keeps working via the `cs` query.
- **Max image long side (px)** — the capture is downscaled only above this (default 1600).

Fixed via the settings file (not exposed in the panel): `PaddingPx` (8), `HideDelayMilliseconds`
(60), `LassoMinDiagonalPx` (12).

## Behavior details

**Search method (settings, default "paste").** Why a browser-mediated flow: as of 2026-08-28
Google gates anonymous image-search processing by client authenticity. Full experiment matrix
(this network, vb_logo.png):

| Session used by the upload | Upload client | Results page |
|---|---|---|
| none | curl / HttpClient | "изображение повреждено" (legacy endpoint) / "ничего не найдено" (`v3/upload`) |
| self-minted by curl or .NET (warm-up GET) | same client | "запрос больше не действителен" |
| minted by WebView2 (Edge engine) or headless Chrome — fresh, or warmed up with organic activity | curl | "запрос больше не действителен" |
| minted by an **interactive visible Chrome** that had just performed a real Lens search in-page | curl | **full results ("Обзор от ИИ" + matches), anonymously** |

So there is no documented or undocumented HTTP endpoint a plain `HttpClient` can use, and
sessions minted by background processes are not trusted either — the verdict follows the
session, and the gate is adaptive (it killed the legacy endpoint for every third-party tool,
Brave's right-click search included). Faking the client (curl-impersonate, ClientHello surgery,
session farming) is a treadmill against exactly this system. The robust path is to let the
user's own browser deliver the image:

1. The cropped PNG is placed on the clipboard (DIB + `PNG`/`image/png` registered formats).
2. `https://lens.google.com` opens in the default browser.
3. Starting ~0.9 s after opening, the plugin sends `Ctrl+V` to the foreground window every
   ~0.8–2 s (up to 5 attempts) — each attempt guarded: only sent while the foreground process
   is the launched browser. Early attempts land while the page is still loading and are dropped
   by the browser; the first attempt that hits the ready page starts the search. Typically the
   search runs ~2 s after release. If every attempt is missed (very slow page load), the image
   stays on the clipboard — press `Ctrl+V` on the Lens page yourself.

The **session-farm fast path is kept wired** (`LensSessionManager` + `WebView2SessionFarmer` +
`FallbackVisualSearchProvider`, opt-in "auto"/"upload" modes): if Google ever trusts
background-minted sessions again, the upload path activates with no code changes. It is not the
default because a poisoned upload still succeeds at HTTP level and cannot trigger the paste
fallback. A possible future improvement is probing `lens.google.com/qfmetadata?vsrid=…` (seen in
the UI's network capture) to detect the poisoned state and fall back automatically.

Consequence: in paste mode the clipboard holds the last captured image (it is not restored).
Lens results render in the browser's own Google session (if you are logged in, results are
personalized) instead of the anonymous session the upload path produced.

- Cancel: `Esc`, right-click, a plain click without dragging, losing window activation, or
  pressing the hotkey again while the overlay is open.
- The hotkey is ignored while the search is in its ~6 s paste window.
- Crops are physical pixels end-to-end; the overlay thread runs
  `SetThreadDpiAwarenessContext(PMv2)` so mixed-DPI multi-monitor setups capture the exact
  region shown (see manual matrix below).
- UAC/protected windows capture as black/blank — a Windows limitation, not a crash.

## Performance

Targets from PLAN.md; measure with release-build timestamps (20 runs) and record actuals here:

- Hotkey → frozen overlay visible: target ≤ 100 ms median, ≤ 180 ms p95 — *actual: TBD (manual)*
- Pointer release → upload dispatched: target ≤ 50 ms — *actual: TBD (manual)*
- Pointer release → results URL in browser: target ≤ 1.5 s median — *actual: live smoke POST
  completed in well under 1 s (2026-08-28)*

## Manual acceptance matrix (still to run on the dev machine)

1. Hotkey → lasso → relevant Lens results in the default browser, from a non-browser app.
2. Over a Chromium browser: pixels-only capture.
3. Sizes: tiny object, large object, text-heavy image, selections touching each edge,
   near-fullscreen.
4. Scaling 100/125/150/200 % on the primary display; second monitor left/right with different
   scale factors and negative coordinates — crops must align with what was circled.
5. Lifecycle: cancel before/during drawing, repeated hotkey, 10 consecutive searches,
   "Reload Plugin Data" with the overlay open, Flow restart (`plugin.log` must show the
   `disposing:` line on every reload — no leaked hotkey or STA thread).
6. Failure paths: offline at start, connection lost during upload — visible error, no hang.
7. Hygiene: no captured image on disk, clipboard unchanged, no image bytes in logs.
