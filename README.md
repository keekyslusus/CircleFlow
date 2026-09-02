# CircleFlow

[Circle to Search](https://search.google/ways-to-search/circle-to-search/) for Windows

CircleFlow opens a frozen view of the monitor under the pointer. Windows OCR runs locally after the overlay appears, allowing recognized words to be selected with the mouse and copied or searched. Hold `Alt` when starting on text to force the original color-pick or visual-lasso gesture.

The Translate action sends recognized text, one bounded segment at a time, to the MyMemory HTTPS API after a one-time consent prompt. Screenshots are never sent. MyMemory may retain and process submitted segments under its [terms](https://mymemory.translated.net/terms-and-conditions). Translation can be canceled or removed without closing the overlay.

## Build and test

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet build .\CircleFlow.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

## License

This project is distributed under GPL-3.0-or-later. See `LICENSE`, `THIRD_PARTY_NOTICES.txt`, and `THIRD_PARTY_LICENSES`.
