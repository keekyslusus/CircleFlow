- readme wip

# CircleFlow

[Circle to Search](https://search.google/ways-to-search/circle-to-search/) for Windows

## Screen text recognition

CircleFlow automatically recognizes Russian and English text on the same frozen screen. It uses the installed Windows OCR language packs (`ru-RU`/`ru-*` and `en-US`/`en-*`); at least one of those packs must be installed. Large displays are processed in overlapping full-resolution tiles instead of being downscaled.

Selecting text is immediate. The first Copy or Search action rechecks each selected line from an enlarged crop of the original frozen frame, then caches that result for later actions on the same selection. If this refinement cannot produce text, CircleFlow safely uses the preliminary selection text.


## Build and test

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet build .\CircleFlow.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

## License

This project is distributed under GPL-3.0-or-later. See [`LICENSE`](./LICENSE), [`THIRD_PARTY_NOTICES.txt`](./THIRD_PARTY_NOTICES.txt), and [`THIRD_PARTY_LICENSES`](./THIRD_PARTY_LICENSES).
