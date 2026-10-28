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

## Publishing a release

1. Raise `<Version>` in `CircleFlow.csproj`. It must be higher than the latest release, and its tag `v<version>` must not exist yet.
2. Commit/push to `master`.
3. Run `release` workflow on `master`.

Things to keep in mind:

- A published release cannot be rolled back: Velopack never downgrades, so deleting a release does not bring anyone back. Ship a fix as a higher version.
- The installer is not code-signed, so SmartScreen warns people who download `Setup.exe`. Updates are not affected.
