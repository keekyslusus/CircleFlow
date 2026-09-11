- readme wip

# CircleFlow

[Circle to Search](https://search.google/ways-to-search/circle-to-search/) for Windows


## Build/test

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet build .\CircleFlow.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

The plugin uses Flow Launcher's `WinRT.Runtime.dll`; the build intentionally excludes
its own copy. When updating an existing installation, remove any leftover
`WinRT.Runtime.dll` from this plugin's folder (leave Flow Launcher's own copy intact)
and restart Flow Launcher. A private copy can crash the trace.moe video preview.

## License

This project is distributed under GPL-3.0-or-later: [`LICENSE`](./LICENSE), [`THIRD_PARTY_NOTICES.txt`](./THIRD_PARTY_NOTICES.txt), [`THIRD_PARTY_LICENSES`](./THIRD_PARTY_LICENSES)
