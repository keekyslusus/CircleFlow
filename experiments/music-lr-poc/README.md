# Music L-R proof of concept

Offline experiment for a later app change. Nothing here is wired into CircleFlow. Audio is not in git (see `.gitignore`).

Run every script from this directory. Paths such as `songs/`, `speech/`, `mixes/`, `sig/`, and `win/` are relative to that working directory.

## Goal

Improve Shazam recognition of quiet background music under centered speech, the typical vlog case.

Current CircleFlow pipeline:

1. WASAPI loopback capture.
2. Downmix to mono `(L+R)/2` in `CTS/MusicRecognition/Audio/MonoSampleProvider.cs`, then `CTS/MusicRecognition/Audio/AudioPreprocessor.cs`.
3. Resample to 16 kHz (`AudioPreprocessor.TargetSampleRate`).
4. SongRec-port signature in `CTS/MusicRecognition/Fingerprinting/ShazamSignatureGenerator.cs`.
5. Checkpoints at 4, 8, and 12 seconds in `CTS/MusicRecognition/ProgressiveMusicRecognizer.cs` (`CheckpointSeconds`).
6. POST to the `amp.shazam.com` discovery tag endpoint (`CTS/MusicRecognition/Shazam/ShazamClient.cs`).

Stereo is discarded in `MonoSampleProvider`, so a centered voice and the music share one channel.

## Method

Eight iTunes 30-second previews are listed in `songs/list.txt` (artist, title, preview URL). Real speech recordings are mixed **mono-centered** (the same samples in L and R) over the original stereo music.

Music sits at -10, -15, -20, and -25 dB below speech RMS, plus a music-only control. Speech and the 0 dB music reference are each normalized to -20 dBFS before the level offset (`prep.py`).

Six songs finished every stream and checkpoint: Queen (Bohemian Rhapsody), Michael Jackson (Billie Jean), Ed Sheeran (Shape of You), Dua Lipa (Levitating), Daft Punk (Get Lucky), and Adele (Rolling in the Deep). Imagine Dragons and Coldplay are in the list. Shazam HTTP 429s stopped the run before those two were complete, so `analyze.py` leaves them out of `results.csv`. Partial Imagine Dragons rows remain in `raw_results2.csv`.

Signatures use Python `shazamio_core` (same algorithm family as SongRec). The speech-muting variant uses Silero VAD.

Pipeline:

1. `prep.py` mixes local files. It reads `songs/s1.m4a` through `songs/s8.m4a` and `speech/raw1.mp3` through `speech/raw8.mp3` (speech taken from 90 s, with pauses capped). It does not download. Preview URLs are only in `songs/list.txt`. Outputs: `mixes/<name>.wav` (44.1 kHz stereo), `mixes/<name>_16k.wav` (mono 16 kHz), and `mixes/meta.json`.
2. `signals.py` builds three 16 kHz streams and writes a clip per checkpoint (4, 8, 12 s) under `sig/`:
   - `mono`: `(L+R)/2`
   - `side`: `(L-R)/2`, loudness-normalized so side RMS over the first 12 s matches mono RMS over that same span
   - `vad`: mono with Silero speech regions attenuated by 35 dB
   It also writes `sig/info.json` (`side_to_mid_db`, VAD speech fraction per checkpoint).
3. `run2.py` POSTs each signature to `https://amp.shazam.com/discovery/v5/.../tag/...`. It waits until 10 s have passed since the previous POST, backs off on HTTP 429 (starts at 60 s, doubles, caps at 600 s), and stops a `(file, stream)` pair at the first title hit, the same early-exit idea as CircleFlow. Rows go to `raw_results2.csv`. A console trace is in `run2.log`.
4. `analyze.py` keeps files where mono, side, and VAD each either matched or used all three checkpoints, and writes `results.csv`.

`signals_lib.py` and `signals_b2.py` are a softer mute trial (15 dB, higher VAD threshold) that would write `sig/*_vad2_*.wav` and `sig/info_vad2.json`. The table below is the 35 dB `signals.py` run, not `vad2`.

`run2.py` also opens `raw_results.csv` and copies any earlier mono checkpoint rows (`reused=1` in `raw_results2.csv`). That earlier file is not in the tree. Re-running `run2.py` needs it, or a small local edit, before it will start.

Requirements: `ffmpeg`, and Python packages `shazamio_core`, `aiohttp`, `numpy`, `silero-vad`, `onnxruntime`, `torch`.

## Results

Matches among the six completed songs. "Mono then side" is column `A_match`: try side only after mono has missed, and count a hit at the earlier of the two.

| Music level vs speech | Mono | Mono then side | VAD mute |
| --- | --- | --- | --- |
| No speech | 6/6 | 6/6 | 6/6 |
| -10 dB | 6/6 | 6/6 | 6/6 |
| -15 dB | 5/6 | 6/6 | 4/6 |
| -20 dB | 4/6 | 6/6 | 3/6 |
| -25 dB | 0/6 | 5/6 | 2/6 |

Side alone is 5/6 at every level, including music only. The miss is always Billie Jean. That preview is near-mono: `side_to_mid_db` is about -15 dB with no speech (`s2_music_only` in `sig/info.json` is -14.9 dB), and speech pulls the ratio lower still.

Mono and side produced no false matches. VAD produced one wrong match: Dua Lipa at -15 dB returned "Lvtg" by Black Dre (`s4_m-15` vad `cp12` in `raw_results2.csv`).

Request counts at -25 dB, from `results.csv`: mono-then-side used 26 requests (`A_req`) and mono used 18 (`base_req`), about 4.3 versus 3 per track. Side is an extra POST only after mono misses, and at -25 dB mono misses every checkpoint, so most tracks pay for both streams.

VAD marked about 86-94% of frames as speech on the mixes that include speech (`vad_speech_frac` in `sig/info.json` and `results.csv`, commonly 86-91% on the 12 s window). Muting those frames removed most of the music, so VAD did worse than the mono baseline once the music was quiet.

## Recommendation for the app

After a mono checkpoint returns no match, also send a side `(L-R)` signature. Loudness-normalize that side signal to the mono RMS. Raw side is much quieter than mid (about 10-40 dB down in `side_to_mid_db` here), and Shazam will not see it if the samples stay at that level.

Skip the side request when the capture is near-mono. A very low side/mid RMS ratio is the hint. Below about -30 dB with no speech means there is almost no stereo difference to recover, so the extra request is wasted. Billie Jean shows the failure mode: side never matched, and the no-speech ratio was already about -15 dB.

Keep using `CTS/MusicRecognition/ShazamRequestThrottle.cs` and honor HTTP 429. This run slept 60 s and longer on 429s. Extra side requests make that limit easier to hit (26 versus 18 at -25 dB).

Where to change:

- Keep the stereo capture through `AudioPreprocessor`. Today `MonoSampleProvider` folds every channel to `(sum)/N` before the 16 kHz buffer exists, so L and R are gone before `ProgressiveMusicRecognizer` runs.
- Extend the checkpoint loop in `ProgressiveMusicRecognizer` (4/8/12 s). On a mono miss, build the normalized side signature with `ShazamSignatureGenerator` and send it through the existing Shazam client, still subject to the throttle.

## Caveats

- Inputs are 30 s iTunes previews, not full tracks. Offsets in `raw_results2.csv` often land outside the preview, which is normal for Shazam alignment against the full recording.
- The mix is synthetic. Speech is sample-identical in L and R, which is the ideal case for `(L-R)` cancelling the voice.
- Real videos can have a voice that is not centered, stereo reverb on the voice, or heavy compression and limiting. Any of those leak speech into the side channel or crush the music.
- The completed sample is six songs. Two further previews never finished because of rate limits.
