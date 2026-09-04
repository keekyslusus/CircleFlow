- readme wip

# CircleFlow

[Circle to Search](https://search.google/ways-to-search/circle-to-search/) for Windows

## Screen text recognition

CircleFlow automatically uses every Windows OCR language pack installed on the system; there is no manual source-language selector. At least one OCR language pack must be installed. Large displays are processed in overlapping full-resolution tiles instead of being downscaled, with pixel preparation and recognition limited to two concurrent operations across all languages. Installing more language packs can therefore increase recognition time.

Selecting text is immediate. The first Copy or Search action rechecks each selected line from an enlarged crop of the original frozen frame, then caches that result for later actions on the same selection. If this refinement cannot produce text, CircleFlow safely uses the preliminary selection text.


## Build and test

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet build .\CircleFlow.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

## License

This project is distributed under GPL-3.0-or-later. See [`LICENSE`](./LICENSE), [`THIRD_PARTY_NOTICES.txt`](./THIRD_PARTY_NOTICES.txt), and [`THIRD_PARTY_LICENSES`](./THIRD_PARTY_LICENSES).
