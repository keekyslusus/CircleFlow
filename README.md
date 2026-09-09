- readme wip

# CircleFlow

[Circle to Search](https://search.google/ways-to-search/circle-to-search/) for Windows


## Build and test

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet build .\CircleFlow.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

The plugin uses Flow Launcher's `WinRT.Runtime.dll`; the build intentionally excludes
its own copy. When updating an existing installation, remove any leftover
`WinRT.Runtime.dll` from this plugin's folder (leave Flow Launcher's own copy intact)
and restart Flow Launcher. A private copy can crash the trace.moe video preview.

## Screen translation

The Translate action sends the original captured screen to Google Translate's unofficial image endpoint
after screenshot-sharing consent. It does not depend on local OCR or require an API key, account or backend.
The translated image replaces the frozen screen; Show original restores the original capture. Within the
same overlay, Translate shows the cached image again without a network request when the target language
is unchanged. Changing the target starts a new translation of the original capture; closing the overlay
releases its cached result. Text selection
runs Windows OCR on the displayed image using the selected translation target (its OCR pack must be installed).

A separate, invisible WebView2 loads Google's small service frame and current signing code. Screenshot
uploads and image responses use HttpClient, without loading the Translate application UI. The worker is
created on demand, reused for nearby requests and closed after two idle minutes. Network requests have a
45-second operation timeout; closing/canceling the overlay prevents stale results from replacing its image.
This is an unofficial integration: Google can change or reject requests. An image-only response is treated
as an unconfirmed result, rather than incorrectly reporting a successful translation or a definite no-text case.

## Translation memory profiling

Set `TranslationMemoryProfilingEnabled` in `CTS/CompositionRoot.cs` to `true` and rebuild to enable
profiling. It defaults to `false`; no profiler instance, sampling timer or diagnostic writes are created.

Diagnostic snapshots are written to `translation-memory.jsonl` beside `plugin.log` in the installed
plugin directory. Action markers distinguish overlay open/close, translation requests, encoding,
WebView2 initialization, response decoding, original/cached-image display and OCR completion.
One-second samples continue for three minutes after the last marker, including WebView2's idle shutdown.
The file rotates at 5 MiB to `translation-memory.jsonl.old`; send both files when diagnosing a longer run.

Counters are bytes: host PrivateBytes (private committed memory), WorkingSetBytes (resident pages),
managed live memory without forcing GC, heap size at the last GC and allocation/collection counters.
Only WebView2 processes reported by the translation environment are tracked, with PID/start-time checks.
Their individual working sets can contain shared pages and must not be summed as unique physical RAM.
The host counters cover ALL of Flow Launcher and its plugins; stage deltas estimate this feature's impact,
not an exact per-plugin allocation. Sampling can miss subsecond peaks and introduces a small measurement
overhead. No screenshots, recognized text or signing tokens are logged, and no forced GC is performed.

## License

This project is distributed under GPL-3.0-or-later. See [`LICENSE`](./LICENSE), [`THIRD_PARTY_NOTICES.txt`](./THIRD_PARTY_NOTICES.txt), and [`THIRD_PARTY_LICENSES`](./THIRD_PARTY_LICENSES).
