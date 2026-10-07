# Contributing to CircleFlow

## Setup

Development requires Windows and the .NET 9 SDK. Browser features need the [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download); OCR tests need at least one Windows OCR language pack.

Code conventions (composition, one responsibility per class, strings in `Assets/Languages/*.xaml`, colors in `PluginPalette`, icon style) are in [AGENTS.md](./AGENTS.md). They apply to people as well as clanker agents.

## Build and test

```powershell
dotnet build .\CircleFlow.csproj -c Release
dotnet test .\tests\CircleToSearch.Tests --filter "Category!=Slow"
dotnet test .\tests\CircleToSearch.Tests
```

The first test command is the fast set (about 15 seconds) for iterating; the second is the full suite to run before a pull request. Mark a test class `[Trait("Category", "Slow")]` when its tests together take a second or more. Tests marked `Category=Live` and preview tests guarded by `CTS_*` environment variables need external services or an interactive setup; they return without checking anything unless their variable is set, for example `dotnet test .\tests\CircleToSearch.Tests --filter "Category=Live" -e CTS_LIVE=1`.

If a running `CircleFlow.exe` locks the build output, exit it (`Stop-Process -Name CircleFlow`) and build again.

## Application layout

`dotnet build` keeps the standard development layout, with `Data` beside `CircleFlow.exe`. `dotnet publish` and the release script produce the release layout: the root holds `CircleFlow.exe`, application assets and licenses, and `deps` holds the managed application, the self-contained .NET runtime, `WinRT.Runtime.dll`, native libraries and dependency translations. Developer XML documentation is excluded. Publish into a fresh directory to avoid leftovers from older builds.

An installed copy lives in `%LocalAppData%\CircleFlow`:

- `current` - the application; every update replaces it completely.
- `Data` - settings, logs, browser profiles and temporary files. `AppPaths` puts it next to `Update.exe` rather than inside `current`, so updates keep it.
- `Update.exe` and `packages` - Velopack's updater and its downloaded packages.

The portable zip has the same layout in any folder, marked by a `.portable` file. Velopack keeps its own logs in `%LocalAppData%\velopack`.

## Updates

`UpdateService` checks the GitHub releases of this repository a minute after start and then daily (hourly after a failed check). A newer release is offered in the app's own notification window, not a Windows toast, so the offer appears even when Windows notifications or Do Not Disturb are off. The card stays until it is closed or CircleFlow starts a capture; a later check does not stack a second card for the same version. **Update** downloads the delta or full package, exits CircleFlow gracefully and lets `Update.exe` install the release and restart the app. Copies without `Update.exe`, such as the development build, never check.

Uninstalling runs the `--veloapp-uninstall` hook, which removes the autostart entry if it points at this copy, then deletes the whole install folder including `Data`.

## Shutdown

Normal shutdown waits for cleanup. If it stays stuck for 10 seconds, the watchdog tries to remove the tray icon and close the activation channel, then terminates the process with an error code. Windows logoff and shutdown are never cancelled: the session-ending handler gives asynchronous cleanup up to two seconds, then continues shutting down even if that work is unfinished. Diagnostic logs are in `Data\Logs`.

## Release build

```powershell
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

The script works from any directory. It publishes a self-contained x64 build into a fresh `bin\publish\stage-<id>\CircleFlow` folder and packs it with Velopack (`dotnet vpk`, pinned in `.config\dotnet-tools.json`) into `bin\releases`: `CircleFlow-win-Setup.exe`, `CircleFlow-win-Portable.zip`, the full package, a delta when the previous version's package is present, and the `releases.win.json` feed. Rebuilding a version that `bin\releases` already holds clears that folder first, because `vpk` does not pack an existing version again. Existing build output and user `Data` are never packed.

### Checking a release build

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\Test-PublishedApp.ps1 -ArchivePath .\bin\releases\CircleFlow-win-Portable.zip
```

Exit CircleFlow first. The script verifies the portable zip (no stray `Data`, logs, settings or test files; every required asset and license present; bundled assets identical to their sources; a self-contained runtime), extracts it under `tests\temp` into a path with spaces and Cyrillic letters, and starts it from another working directory with no installed .NET available. A test-only startup hook then loads strings and the icon, extracts the bundled extension, runs OCR, creates regular and composition WebView2 controls, checks that .NET and WinRT load from the release folder, and raises the managed session-ending event to confirm CircleFlow does not cancel it; it never requests a real logoff. The report is written to the extracted copy's `Data\Temp\publish-probe.json`. The hook is not part of the release.

This check does not cover external search, translation or music providers, or multi-monitor and DPI behavior.

### Trying the update flow

Locally, without publishing anything: uninstall CircleFlow and run

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\Test-Update.ps1
```

It builds `<version>-update.1` with `-UpdateFeed tests\temp\update-feed`, installs it, builds `<version>-update.2` into the same feed and starts the app. About a minute later a CircleFlow notification offers the update.

Through GitHub: build with `.\build_release.ps1 -UpdateRepository https://github.com/<owner>/<test-repo> -Version <version>`, install that build, then build a newer version the same way and publish it with `dotnet vpk upload github --repoUrl <repo> --token <token> -o bin\releases --publish --tag v<version>`. The test repository must be public, because the app reads releases without a token.

Both options bake the test source into the build as assembly metadata; normal and CI builds always use this repository. Start test installers from Explorer: an installer started from a packaged app, such as a terminal inside the Claude desktop app, writes into that app's virtualized `AppData`, and Windows then cannot uninstall it.

## Publishing a release

1. Raise `<Version>` in `CircleFlow.csproj`. It must be higher than the latest release, and its tag `v<version>` must not exist yet.
2. Commit/push to `master`.
3. Run `release` workflow on `master`.

The workflow downloads the latest release to build a delta against it, runs `build_release.ps1`, and publishes the release `v<version>` with `vpk upload github`. Installed copies offer it within a day. Add release notes on GitHub afterwards; `vpk` creates the release without a description.

Things to keep in mind:

- A published release cannot be rolled back: Velopack never downgrades, so deleting a release does not bring anyone back. Ship a fix as a higher version.
- The installer is not code-signed, so SmartScreen warns people who download `Setup.exe`. Updates are not affected.
