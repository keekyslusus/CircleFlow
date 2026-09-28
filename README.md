- README WIP

# CircleFlow

Install with `CircleFlow-win-Setup.exe` from the latest release. It needs no administrator rights and puts everything in `%LocalAppData%\CircleFlow`: `current` holds the application, `Data` holds your settings, and `Update.exe` installs updates. `CircleFlow-win-Portable.zip` is the same application for a folder of your choice.

The release includes its own .NET runtime and `WinRT.Runtime.dll`; no separate .NET installation is required. Inside `current`, the root contains `CircleFlow.exe`, application assets and licenses; `deps` contains the managed application, .NET runtime, native libraries and dependency translations. Developer XML documentation is excluded.

Browser features require the [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download). It is an external prerequisite and is not bundled. OCR languages depend on the recognition packs installed in Windows. Search, translation and music recognition require an internet connection.


## Data and updates

Settings, logs, browser profiles and application temporary files are stored under `Data` next to `current` and `Update.exe`, so updates, which replace `current`, keep them. A copy run without `Update.exe`, such as the development build, keeps `Data` beside `CircleFlow.exe`. The folder must be writable.

CircleFlow checks the GitHub releases a minute after it starts and then once a day. When a newer version is out, a Windows notification offers **Update**; CircleFlow then downloads it (only the changed files when possible), exits, installs it and starts again. The portable copy shows the same offer in its own window. Uninstalling from Windows Settings removes the whole folder, including `Data`. Velopack, the installer and updater, keeps its own logs in `%LocalAppData%\velopack`.

**Run at Windows startup** in Settings adds a `CircleFlow` value to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` that points to this copy of `CircleFlow.exe`; it is off until you turn it on. Uninstalling removes it; turn it off before moving or deleting a portable folder, or the entry is left behind. Disabling CircleFlow in Task Manager's Startup apps is shown as off in Settings.

Normal shutdown waits for cleanup. If shutdown remains stuck for 10 seconds, CircleFlow attempts to remove its tray icon and close its activation channel, then terminates its own process with an error code. Diagnostic logs are in `Data\Logs`.

Windows logoff and shutdown are never cancelled by CircleFlow. The session-ending handler keeps the dispatcher available for up to two seconds of asynchronous cleanup, then proceeds with application shutdown even if that work is unfinished. The watchdog remains the fallback for blocked synchronous cleanup.

## Build/test

Development requires Windows and the .NET 9 SDK.

```powershell
dotnet build .\CircleFlow.csproj -c Release
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --filter "Category!=Live"
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

Ordinary `dotnet build` output keeps the standard development layout. `dotnet publish` and the release script produce the clean `deps` layout; publish into a fresh directory to avoid leftover files from older builds.

The release script works from any working directory. It publishes a self-contained x64 application into a new `bin\publish\stage-<id>\CircleFlow` folder and packs it with Velopack (`dotnet vpk`, restored from `.config\dotnet-tools.json`) into `bin\releases`: `CircleFlow-win-Setup.exe`, `CircleFlow-win-Portable.zip`, the update packages and the `releases.win.json` feed. When `bin\releases` already holds the previous version's packages, the script also builds a delta update. Each build uses a fresh staging directory; existing build output and user `Data` are not merged into the packages or deleted. The release workflow downloads the previous release for the delta and uploads everything to GitHub with `vpk upload github`.

Verify a release with the installed WebView2 Runtime and at least one Windows OCR language:

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\Test-PublishedApp.ps1 -ArchivePath .\bin\releases\CircleFlow-win-Portable.zip
```

This check extracts a separate copy under `tests\temp`, checks its packaged assets, and launches that copy of `CircleFlow.exe` from a different working directory. A test-only .NET startup hook initializes OCR, WebView2 and WebView2CompositionControl in the application's default load context, then raises the managed session-ending event and verifies that CircleFlow does not cancel it. It never requests a real Windows logoff or shutdown. It checks the bundled .NET and WinRT assemblies and actual browser profile paths. The hook is not included in the release ZIP. The report remains in the extracted copy's `Data\Temp\publish-probe.json`.

To try the update flow without publishing a release, uninstall CircleFlow and run `.\tests\Test-Update.ps1`. It installs a test build that reads updates from `tests\temp\update-feed`, publishes a newer build there and starts the app; about a minute later it offers the update.

To try the same flow through GitHub, build with `.\build_release.ps1 -UpdateRepository https://github.com/<owner>/<test-repo> -Version <version>`, install that build, then publish a newer build there with `dotnet vpk upload github --repoUrl <repo> --token <token> -o bin\releases --publish --tag v<version>`. The repository must be public; the app reads releases without a token. Run the installer from Explorer: an installer started from a packaged app, such as a terminal inside the Claude desktop app, lands in that app's virtualized `AppData`, and Windows cannot uninstall it.

Tests marked `Category=Live` and tests guarded by environment variables need their respective services or interactive setup. The package check uses local browser content; it does not certify external search, translation or music providers, or multi-monitor/DPI behavior.

## License

GPL-3.0-or-later. See [LICENSE](./LICENSE), [THIRD_PARTY_NOTICES.txt](./THIRD_PARTY_NOTICES.txt) and [THIRD_PARTY_LICENSES](./THIRD_PARTY_LICENSES).
