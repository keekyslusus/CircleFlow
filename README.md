# Circle to Search

A Flow Launcher plugin for visual search and zero-configuration music recognition on Windows.

Open the overlay with the configured hotkey or Flow query. Draw around a screen region to search it
with the selected visual provider, or press the separate music-note button to recognize audio playing
on the default Windows output device. Music recognition samples progressively at about 4, 8, and 12
seconds and stops as soon as a track matches.

## Music recognition behavior and privacy

- No API key, backend, microphone access, SongRec, Rust, Python, or ffmpeg is required.
- Audio is captured only from the current default Windows output through WASAPI loopback.
- Raw PCM stays in memory for the active recognition session and is never written to disk by the plugin.
- Only a locally generated acoustic fingerprint is sent to Shazam; raw audio is not uploaded.
- Recognition requires internet access and may return no match even when audio is present.
- The Shazam endpoint is unofficial and can change or rate-limit an IP address. HTTP 429 stops the
  current session and starts a local cooldown before another capture is allowed.

The diagnostic CLI in `poc/MusicRecognition.Poc` can recognize an existing file with `--file` or run
a manual loopback smoke test. Its optional `--save-wav` switch is diagnostic-only and is not used by
the production plugin.

## Build and test

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet test .\poc\MusicRecognition.Poc.Tests\MusicRecognition.Poc.Tests.csproj -c Release
dotnet build .\CircleToSearch.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

## License

This project is distributed under GPL-3.0-or-later. See `LICENSE` and `THIRD_PARTY_NOTICES.md`.
