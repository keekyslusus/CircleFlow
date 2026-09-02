# CircleFlow

[Circle to Search](https://search.google/ways-to-search/circle-to-search/) for Windows

## Build and test

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet build .\CircleFlow.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

## License

This project is distributed under GPL-3.0-or-later. See `LICENSE`, `THIRD_PARTY_NOTICES.txt`, and `THIRD_PARTY_LICENSES`.
