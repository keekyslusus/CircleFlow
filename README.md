- README WIP

# CircleFlow

The release includes its own .NET runtime and `WinRT.Runtime.dll`; no separate .NET installation is required. Keep all files from the archive together.

Browser features require the [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download). It is an external prerequisite and is not bundled. OCR languages depend on the recognition packs installed in Windows. Search, translation and music recognition require an internet connection.


## Data and updates

Settings, logs, browser profiles and application temporary files are stored under `Data` beside `CircleFlow.exe`. The folder must be writable. CircleFlow does not import settings or profiles from Flow Launcher and does not switch to a different data folder if this location is unavailable.

Exit CircleFlow before moving or replacing its files. Keep `Data` when updating; do not delete the bundled runtime DLLs. A future installer may use `%LocalAppData%\Programs\CircleFlow`; the current release is a portable ZIP with no installer, automatic startup or updater.

Normal shutdown waits for cleanup. If shutdown remains stuck for 10 seconds, CircleFlow attempts to remove its tray icon and close its activation channel, then terminates its own process with an error code. Diagnostic logs are in `Data\Logs`.

## Build/test

Development requires Windows and the .NET 9 SDK.

```powershell
dotnet build .\CircleFlow.csproj -c Release
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --filter "Category!=Live"
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

The release script works from any working directory. It publishes a self-contained x64 application into a new `bin\publish\stage-<id>\CircleFlow` folder and creates `bin\releases\CircleFlow-<version>-win-x64.zip`. Each build uses a fresh staging directory; existing build output and user `Data` are not merged into the archive or deleted. Archive entries use a stable order and timestamp.

Verify a release with the installed WebView2 Runtime and at least one Windows OCR language:

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\Test-PublishedApp.ps1 -ArchivePath .\bin\releases\CircleFlow-0.5.1-win-x64.zip
```

This check extracts a separate copy under `tests\temp`, checks its packaged assets, and launches that copy of `CircleFlow.exe` from a different working directory. A test-only .NET startup hook initializes OCR, WebView2 and WebView2CompositionControl in the application's default load context, then requests normal shutdown. It checks the bundled .NET and WinRT assemblies and actual browser profile paths. The hook is not included in the release ZIP. The report remains in the extracted copy's `Data\Temp\publish-probe.json`.

Tests marked `Category=Live` and tests guarded by environment variables need their respective services or interactive setup. The package check uses local browser content; it does not certify external search, translation or music providers, or multi-monitor/DPI behavior.

## License

GPL-3.0-or-later. See [LICENSE](./LICENSE), [THIRD_PARTY_NOTICES.txt](./THIRD_PARTY_NOTICES.txt) and [THIRD_PARTY_LICENSES](./THIRD_PARTY_LICENSES).
