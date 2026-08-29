# Provider picker, in-overlay music recognition, reactive waveform, and ripple system

## Purpose

Implement the `design/chip-provider-music.html` interaction in the production WPF overlay:

- add a persistent Google Lens / Yandex Images provider picker;
- apply a newly selected provider to the current visual-selection session and save it as the next-session default;
- keep the overlay alive while Windows loopback audio is being recognized;
- fade out the frozen screenshot during listening so the dimmed live desktop is visible;
- keep the provider picker usable during listening while lasso input is disabled;
- retain the prototype's current five-dot waveform, but drive it from real audio level and transient data;
- replace the one-off entrance implementation with reusable control-level and scene-level ripple effects;
- emit scene ripples only for significant audio transients and for explicit semantic events such as a music match.

This document is an implementation handoff. It describes the required code changes but does not implement them.

## Verified current behavior

- `CTS/CompositionRoot.cs` registers Google Lens and Yandex Images in `VisualSearchProviderRouter`; both providers and their tests already exist.
- `PluginSettings.SearchProviderId` already stores the default provider and defaults to `SearchProviderIds.GoogleLens`.
- `VisualSearchProviderRouter.Providers` exposes the descriptors needed to build a picker, and `GetEffectiveDescriptor` already provides safe fallback for an unknown saved ID.
- `SearchCoordinator.RunSessionAsync` snapshots `settings.SearchProviderId` before opening the overlay. Consequently, an in-overlay provider change cannot currently affect the active selection.
- `OverlayVisualFactory` currently creates only the selection chip and separate music button. The provider chip/menu, listening view, and result cards exist only in the HTML prototype.
- `OverlayWindow.OnMusicButtonClick` sets `OverlayOutcome.MusicRecognition` and shuts down the overlay dispatcher immediately.
- `SearchCoordinator.RunMusicRecognitionAsync` runs after the overlay closes and surfaces results through Flow Launcher messages.
- `LoopbackCaptureSession` already captures the default Windows output through WASAPI loopback, but only appends raw bytes; it exposes no live audio-level progress.
- `ProgressiveMusicRecognizer` performs serial recognition attempts using 4, 8, and 12 second snapshots. This behavior is covered by tests and must not change.
- `OverlayEntrance` is a static, one-shot wash/particle animation called only when the overlay loads. It is not a reusable ripple host.
- Production UI colors are required to come from `CTS/Ui/PluginPalette.cs`; `PluginPaletteTests` enforces this.
- All user-visible text is required to come from `Languages/*.xaml` through `UiStrings`.

## Locked product decisions

1. The provider selection is persistent.
   - Selecting a provider updates the current overlay immediately.
   - A subsequent lasso completed in that overlay uses the selected provider even if settings persistence has not finished.
   - The same selection is saved as the default for future overlay sessions.

2. The provider menu remains available during listening.
   - Changing provider has no effect on the active music recognition.
   - The new provider is still persisted for the next visual search.
   - Lasso drawing remains disabled for the entire listening/result flow.

3. Entering listening does not close the overlay.

4. The frozen screenshot fades to transparent during listening, revealing the live desktop beneath the existing dim layer. Do not animate `Window.Opacity`; animate content layers only, preserving the existing workaround for black layered-window frames.

5. Keep the prototype's current centered five-dot waveform for this iteration. Do not redesign it into the proposed circular audio aura yet.

6. Ripple has two distinct scopes:
   - control ripple, clipped to the chip/button/menu-item boundary;
   - scene ripple, emitted into the overlay from an element or screen point.

7. Audio-driven scene ripples are emitted only for detected significant transients/onsets, not on every audio buffer or render frame.

## Explicit assumptions

- Starting music recognition commits the session to the music flow. Canceling listening closes the overlay rather than returning to lasso selection. Returning to lasso after a long listening interval would require a fresh screen capture; it is intentionally excluded from this change.
- `Esc`, a repeated invocation hotkey, right-click, or a second click on the listening music button cancels recognition and closes the overlay. When the provider menu is open, the first `Esc` closes only the menu; a second `Esc` cancels the session.
- Closing a result card closes the overlay. `Retry` begins a new recognition attempt in the same live-desktop overlay.
- The provider control stays usable in both `Listening` and `MusicResult` states. Background left-clicks are ignored outside controls in these states.
- Match, no-match, no-audio, rate-limit, device-error, and service-error outcomes are presented inside the overlay. Existing Flow message delegates remain as fallback only if the overlay cannot display an outcome or the URL cannot be opened.
- The current waveform reacts to one scalar audio envelope plus peak/transient information. Frequency-band/FFT-driven deformation is deferred until the waveform design is revisited.
- No new NuGet dependency is needed; NAudio and the current WPF stack are sufficient.

## Target interaction state machine

### `Selecting`

- Frozen screenshot is visible.
- Dim, lasso, selection chip, provider chip/menu, and music button behave normally.
- Provider changes update the chip, affect the current lasso outcome, and request settings persistence.
- Completing a valid lasso emits `VisualSelection(selection, providerId)`, plays the existing selection reveal, closes the overlay, and starts the chosen provider.
- Clicking music transitions to `Listening` without completing the overlay session.

### `Listening`

- Fade out screenshot, existing lasso paths, and selection-only sheen/frame; leave the dim layer visible over the live desktop.
- Disable all lasso mouse capture and stroke updates.
- Keep the provider chip/menu and music button hit-testable.
- Show the centered five-dot waveform and localized `Listening…` label.
- Feed audio frames into the waveform and transient detector.
- Emit scene ripples from the waveform center only on accepted transients.
- Clicking the music button again or canceling the session cancels the recognition token and closes the overlay.

### `MusicResult`

- Stop the waveform render subscription and remove transient subscriptions.
- Keep the live desktop/dim presentation.
- Show the appropriate result pill/card from the HTML reference.
- On a match, emit one semantic scene ripple from the laid-out match chip center; this is independent of audio transient ripples.
- `Open in Shazam` validates the URL with the existing policy, closes the overlay, and then opens the URL.
- `Copy` copies `Artist — Title` without closing the overlay and briefly changes its icon to a localized accessible confirmation state.
- `Retry` transitions back to `Listening` and creates a fresh capture/recognition run.
- `Close` ends the overlay session.

### Terminal states

- `VisualSelection` transfers ownership of the GDI frame to `SelectionOutcome`; the search path disposes it after cropping as it does today.
- Music completion/cancellation and ordinary overlay cancellation dispose the original GDI frame inside the overlay session.
- All pending UI posts and audio progress callbacks become no-ops after session disposal.

## Architecture

### 1. Replace the one-result overlay call with a session proxy

The current `Func<CancellationToken, Task<OverlayOutcome?>>` cannot support music progress or commands after the music click. Add an overlay session abstraction under `CTS/Capture`:

```csharp
public interface IOverlaySession : IAsyncDisposable
{
    Task<OverlayCommand> ReadCommandAsync(CancellationToken cancellationToken);
    Task ShowListeningAsync(CancellationToken cancellationToken);
    Task ReportAudioAsync(MusicVisualizationFrame frame, CancellationToken cancellationToken);
    Task ShowMusicResultAsync(MusicRecognitionOutcome outcome, CancellationToken cancellationToken);
    Task CloseAsync();
}

public interface IOverlaySessionFactory
{
    Task<IOverlaySession?> OpenAsync(
        OverlayLaunchOptions options,
        CancellationToken cancellationToken);
}
```

`OverlayLaunchOptions` contains capture options, localized strings, provider descriptors, and the effective initial provider ID. Keep persistence, recognition, URL opening, and provider routing outside the WPF window.

Use sealed records for commands rather than an implementation class hierarchy. The command payload must cover:

- `ProviderSelected(providerId)`;
- `VisualSelection(selection, providerId)`;
- `StartMusicRecognition`;
- `CancelSession`;
- `RetryMusicRecognition`;
- `OpenMusicResult`;
- `CopyMusicResult` if clipboard work is kept outside the window.

The session implementation owns the STA thread and WPF dispatcher. `OpenAsync` completes after the window and command channel are ready, not after the window closes. Public session methods marshal to the overlay dispatcher with `Dispatcher.InvokeAsync`. Window event handlers only mutate immediate view state and publish commands; they do not call providers, Shazam, settings storage, or Flow APIs.

Use a bounded/latest-value mechanism for audio progress so a slow UI cannot accumulate thousands of frames. Commands such as provider selection and close must use a separate lossless channel and must never be dropped.

### 2. Let `SearchCoordinator` orchestrate the live session

Change `SearchCoordinator` to depend on `IOverlaySessionFactory` and a settings-save delegate rather than the old one-shot selection delegate.

The coordinator loop must continue reading overlay commands while recognition is running. Use `Task.WhenAny` between the next UI command and the current recognition task so provider selection, close, hotkey cancellation, and retry remain responsive.

Required coordinator behavior:

- Resolve the saved provider through `VisualSearchProviderRouter.GetEffectiveDescriptor` when constructing `OverlayLaunchOptions`.
- Do not snapshot provider settings before opening the overlay.
- On `ProviderSelected`:
  - validate/canonicalize the ID through the router;
  - update `settings.SearchProviderId`;
  - persist through the injected save delegate;
  - log and surface a localized nonfatal error if persistence fails;
  - do not interrupt music recognition.
- On `VisualSelection`, route using the provider ID embedded in that command, not the current mutable setting.
- On `StartMusicRecognition`, transition `SearchState` to `RecognizingMusic`, show listening UI, create a linked recognition cancellation source, and start recognition without blocking the command loop.
- Forward visualization progress to `IOverlaySession.ReportAudioAsync` without awaiting every frame on the capture callback thread.
- On recognition completion, ignore a late result if the session or recognition token was canceled; otherwise call `ShowMusicResultAsync` and return coordinator state to a session-active state that still rejects a second independent query.
- On `RetryMusicRecognition`, cancel/dispose the previous run completely before creating the next one.
- On match link action, reuse `SearchCoordinator.IsSafeShazamUrl`; close the overlay before invoking the browser.
- Preserve existing hotkey semantics: a repeated hotkey during `Selecting` or `RecognizingMusic` cancels the active session, while upload remains noncancelable/ignored as today.

Either add `SearchState.ShowingMusicResult` or keep a separate internal overlay mode, but external triggers must not start a second session while a result card is displayed.

### 3. Provider picker model and persistence

Extend `OverlayOutcome`/the new visual-selection command so a visual selection always contains a nonblank provider ID. Validate the invariant in its constructor/factory.

Build the picker from `VisualSearchProviderRouter.Providers`; do not hardcode a two-item menu. The menu excludes the currently selected provider, matching the HTML prototype, and will naturally support future registrations.

Add a small UI-only provider visual catalog for known IDs:

- Google Lens: multicolor Google mark plus short label `Google`;
- Yandex Images: Yandex mark plus short label `Yandex`;
- unknown future provider: neutral search/image icon and descriptor display name.

Do not put WPF geometry or color data into `SearchProviderDescriptor`. Keep domain registration independent of UI. Put all fixed brand and surface colors in `PluginPalette`.

Provider button/menu requirements:

- provider button is a separate element between selection chip and music button;
- menu opens above the provider button and stays inside the overlay visual tree rather than using a service locator;
- opening/closing follows the prototype's scale `0.95 -> 1` and opacity animation, with an immediate reduced-motion fallback;
- menu, result cards, and buttons are included in action-UI hit testing so clicks never begin a lasso;
- provider change updates the local selected ID before publishing the persistence command, guaranteeing that an immediate mouse-up uses the new provider;
- provider changes remain enabled in `Listening` and `MusicResult`;
- tooltips and `AutomationProperties.Name` use localized full provider names.

`CompositionRoot` supplies:

- `providerRouter.Providers`;
- the effective saved provider ID;
- `api.SaveSettingJsonStorage<PluginSettings>` as the persistence delegate;
- the overlay session factory and effect dependencies.

Do not move graph assembly into `Main.cs` or UI factories.

### 4. Live audio-level progress

Add pure progress types under `CTS/MusicRecognition/Audio`, for example:

```csharp
public readonly record struct AudioLevelFrame(
    TimeSpan Elapsed,
    double Rms,
    double Peak);

public readonly record struct MusicVisualizationFrame(
    TimeSpan Elapsed,
    double NormalizedLevel,
    double NormalizedPeak,
    bool IsTransient);
```

Extend `IAudioCaptureSession.Start` and `IMusicRecognizer.RecognizeAsync` with an optional progress sink. Prefer an explicit small interface or delegate whose callback contract states that it is invoked off the UI thread and must return quickly. Do not use an event that can remain subscribed after capture disposal.

In `LoopbackCaptureSession.CaptureAsync`:

- keep writing exactly the same bytes used for fingerprint snapshots;
- calculate RMS and absolute peak from each incoming buffer before or outside the storage lock;
- support the actual WASAPI `WaveFormat` plus IEEE float 32-bit and common PCM 16/24/32-bit formats in a separately testable `AudioLevelMeter`;
- combine channels by energy rather than using only the first channel;
- aggregate/throttle reports to at most about 30 Hz;
- never hold `_sync` while invoking the progress sink;
- catch/log progress-sink failures so a visualization bug cannot terminate capture or recognition.

Do not change checkpoint timing, snapshot contents, silence threshold, signature generation, request serialization, timeout behavior, or throttle behavior in `ProgressiveMusicRecognizer`.

Normalize raw RMS for UI in a testable mapper, using a logarithmic dB scale rather than directly multiplying linear RMS. A suitable initial range is `-60 dBFS -> 0` and `-6 dBFS -> 1`, clamped to `0..1`.

### 5. Transient/onset detection

Add a stateful, sealed `AudioTransientDetector` composed into the music visualization progress path. It is a visual trigger detector, not a claim of full BPM/beat tracking.

Initial deterministic behavior:

- maintain a fast envelope and a slower moving baseline;
- require a minimum normalized level so silence/noise does not trigger;
- accept an onset when the fast envelope rises by a configurable ratio over the slow baseline;
- apply hysteresis/re-arm behavior after the envelope falls;
- enforce a refractory interval around 200-250 ms;
- reset the detector for every retry/new recognition session.

Place thresholds and timing in named constants or an injected options record so tests can use deterministic values. Emit `IsTransient=true` on only one progress frame per accepted onset.

### 6. Reactive five-dot waveform

Create a dedicated sealed WPF visual, for example `AudioWaveformVisual`, and expose it through `OverlayVisual`. Keep it centered at the prototype's approximate `45%` vertical position and scale in DIPs for the current monitor/DPI.

Render with one custom drawing surface (`OnRender`/`DrawingVisual`) rather than five independently storyboarded controls. Subscribe to `CompositionTarget.Rendering` only while listening and always unsubscribe on result, cancellation, window close, and dispatcher shutdown.

Retain the prototype's five-dot composition and phase offsets, but apply real audio data:

- smooth normalized level with fast attack and slower release;
- level increases vertical excursion, dot radius, and opacity within conservative capped ranges;
- normalized peak briefly increases brightness/scale;
- a transient adds a short decaying impulse without changing the scene-ripple acceptance logic;
- preserve the existing staggered sinusoidal motion so silence still reads as an active listening state;
- avoid phase discontinuities when audio frames arrive irregularly;
- the render loop reads only the latest frame and never blocks on audio/capture locks.

When WPF animations are disabled or High Contrast is active, render a static accessible five-dot indicator and localized label; do not emit animated scene or control ripples.

### 7. Reusable two-scope ripple system

Replace direct calls to `OverlayEntrance.Begin` with composed effect hosts under `CTS/Ui/Effects` or `CTS/Capture/Effects`. Do not introduce an application-owned base-class hierarchy or global singleton.

Use shared immutable configuration records and two sealed hosts:

#### Control ripple host

- attaches to an individual button, chip, or menu item;
- starts at pointer-down coordinates;
- clips to the target's rounded rectangle or ellipse;
- renders below foreground content and never participates in hit testing;
- follows the prototype timing: expansion about 225 ms, minimum hold about 290 ms, fade about 200 ms;
- optionally renders bounded sparkle particles;
- cancels and cleans up on unload;
- is used by provider chip/menu items, music button, result actions, close, retry, copy, and Shazam link.

#### Scene ripple host

- owns a full-overlay effects layer above dim/lasso visuals and below waveform/action/result content;
- accepts an origin point already transformed into root coordinates plus a named preset and normalized intensity;
- computes the radius to the farthest overlay corner;
- caps simultaneous active ripples and reuses visual objects to avoid allocation spikes;
- supports entrance, audio transient, and music-match presets;
- has deterministic cleanup and ignores emissions after disposal;
- is not tied to `OverlayWindow` event names, so other plugin surfaces can host it later.

Suggested API shape:

```csharp
public enum SceneRipplePreset
{
    Entrance,
    AudioTransient,
    MusicMatch,
}

public readonly record struct SceneRippleRequest(
    Point Origin,
    SceneRipplePreset Preset,
    double Intensity);

public interface ISceneRippleSink
{
    void Emit(SceneRippleRequest request);
}
```

Keep timing/geometry profiles separate from color profiles. All colors, including Google/Yandex brand colors, M3 menu/result surfaces, ripple fills, sparkle colors, and waveform colors, belong in `PluginPalette`.

Port the existing entrance wash/particles as the `Entrance` scene preset, preserving the current pointer origin. Delete `OverlayEntrance.cs` only after all callers/tests use the new host; do not leave two competing scene-effect implementations.

For audio:

- origin is the current center of `AudioWaveformVisual`, transformed into the scene layer;
- intensity maps from transient strength/normalized peak to a bounded profile range;
- do not emit more frequently than the detector's refractory interval;
- cap active audio ripples so repeated transients cannot flood full-screen WPF rendering.

For a match:

- wait until the result chip has completed layout;
- transform its center to scene coordinates;
- emit one `MusicMatch` ripple, then animate the chip entrance;
- do not emit success ripple for no-match/error cards.

### 8. Overlay visual tree and screenshot transition

Extend `OverlayVisual` with explicit references rather than searching the tree at runtime:

- provider button, provider content presenter, and provider menu;
- a common action-UI root used by hit testing;
- screenshot transition layer;
- scene ripple layer/host;
- listening layer and `AudioWaveformVisual`;
- result host and current result controls;
- music button state visuals.

Recommended root z-order:

1. frozen screenshot;
2. dim/reveal/lasso/selection layers;
3. scene ripple layer;
4. listening waveform;
5. action tray and in-tree provider menu;
6. result card/action layer, with the provider menu raised above it when open.

On `Selecting -> Listening`:

- stop lasso sampling and release mouse capture;
- clear any queued reveal render callback;
- mark selection input disabled before starting animations;
- fade screenshot opacity `1 -> 0` and selection-specific paths to zero over roughly 180-220 ms;
- do not fade the dim layer;
- show listening UI in parallel;
- leave the window transparent and never toggle layered-window styles mid-flight.

The original GDI bitmap remains owned by the session until terminal close so cancellation and disposal remain deterministic. Since this plan does not return from music to lasso, no stale-frame recapture is needed.

### 9. Music result presentation

Port the result components from `design/chip-provider-music.html`:

- match: compact pill containing music icon, `Title — Artist`, copy, safe Shazam link, close;
- no match: state card with music-off icon and `Try again`;
- no audio: state card with no-sound icon and `Retry`;
- service/device errors: the same state-card shell with localized text, retry, and close;
- rate limited: state-card shell with localized text and close; do not immediately retry into the known cooldown.

Keep result content minimal. Do not add cover-art or metadata fetches. Use fields already returned by `ShazamRecognition` and preserve safe-URL validation.

All result/card text, tooltips, accessible names, `Listening…`, `Try again`, `Retry`, `Copy track info`, `Copied`, provider tooltip formatting, and close actions require keys in `Languages/en.xaml` and typed accessors in `UiStrings`.

## File-by-file implementation map

### Existing files to change

- `CTS/CompositionRoot.cs`
  - construct the overlay session factory, effect configuration, provider launch model, and settings-save delegate;
  - pass router descriptors and effective saved provider into each overlay session;
  - keep all runtime graph assembly here.

- `CTS/Search/SearchCoordinator.cs`
  - replace the one-shot overlay delegate with session orchestration;
  - process provider commands while recognition runs;
  - persist provider changes;
  - route visual selections using their embedded provider IDs;
  - present music outcomes in the overlay and retain fallback Flow error reporting.

- `CTS/Search/SearchProviderId.cs`
  - retain provider IDs/descriptors as UI-independent domain records; only add helpers if canonical validation is needed.

- `CTS/Capture/SelectionOutcome.cs`
  - replace/extend `OverlayOutcome` with provider-aware visual selection and/or the new overlay command records;
  - enforce bitmap/provider invariants.

- `CTS/Capture/OverlayWindow.cs`
  - become the UI endpoint of a session rather than closing on the first music click;
  - publish commands, manage view state, block lasso during music, and own deterministic frame/window cleanup;
  - keep provider interactions active during listening.

- `CTS/Capture/OverlayVisualFactory.cs`
  - build provider chip/menu, listening layer, result layer, control ripple wrappers, and scene effect layer;
  - expose direct references via `OverlayVisual`;
  - keep geometry construction and visual composition out of `OverlayWindow`.

- `CTS/Capture/OverlayEntrance.cs`
  - migrate behavior into the new entrance scene-ripple preset, then remove this file.

- `CTS/MusicRecognition/MusicRecognitionOutcome.cs`
  - add the visualization progress contract/overload to `IMusicRecognizer` without coupling it to WPF.

- `CTS/MusicRecognition/ProgressiveMusicRecognizer.cs`
  - accept/forward live visualization progress while preserving all recognition semantics.

- `CTS/MusicRecognition/Audio/LoopbackCaptureSession.cs`
  - calculate and report throttled level frames from incoming audio buffers.

- `CTS/Ui/PluginPalette.cs`
  - add provider, menu/card, waveform, control ripple, and scene ripple palettes for light/dark themes.

- `CTS/Ui/UiStrings.cs` and `Languages/en.xaml`
  - add every new user-visible/accessibility string.

- `tests/CircleToSearch.Tests/SearchCoordinatorTests.cs`
  - replace the selection delegate harness with a fake overlay session and add concurrent command/recognition scenarios.

- `tests/CircleToSearch.Tests/OverlayWindowTests.cs`
  - update music-click expectation from immediate shutdown to listening state and add provider/menu/lasso-block tests.

- `tests/CircleToSearch.Tests/ProgressiveMusicRecognizerTests.cs`
  - update fakes for progress and prove existing 4/8/12 behavior is unchanged.

- `tests/CircleToSearch.Tests/ChipPreviewTests.cs`, `SelectionPreviewTests.cs`, and `FadeCaptureTests.cs`
  - add deterministic visual states and verify the screenshot-to-live content fade without animating window opacity.

- `tests/CircleToSearch.Tests/PluginPaletteTests.cs`, `UiStringsTests.cs`, and `TestUiStrings.cs`
  - cover new palette roles and translation accessors.

### Suggested new files

- `CTS/Capture/OverlaySession.cs`
- `CTS/Capture/OverlaySessionFactory.cs`
- `CTS/Capture/OverlayCommand.cs`
- `CTS/Capture/OverlayLaunchOptions.cs`
- `CTS/Capture/ProviderVisualCatalog.cs`
- `CTS/Capture/AudioWaveformVisual.cs`
- `CTS/MusicRecognition/Audio/AudioLevelFrame.cs`
- `CTS/MusicRecognition/Audio/AudioLevelMeter.cs`
- `CTS/MusicRecognition/Audio/AudioLevelNormalizer.cs`
- `CTS/MusicRecognition/Audio/AudioTransientDetector.cs`
- `CTS/Ui/Effects/ControlRippleHost.cs`
- `CTS/Ui/Effects/SceneRippleHost.cs`
- `CTS/Ui/Effects/RippleModels.cs`
- corresponding focused test files for pure audio/ripple/session logic.

Names may be adjusted to existing namespace conventions, but responsibilities must remain separated. Do not introduce an abstract UI base class, implementation inheritance, service locator, or static global effect manager.

## Edge cases and failure handling

- Unknown/empty saved provider ID: show the router's effective default; a user selection writes a canonical registered ID.
- Provider persistence failure: keep the current session's local choice, log the exception, surface a localized nonfatal error, and allow selection/music to continue.
- Provider changed immediately before lasso mouse-up: the command embeds the already-updated local ID, so the current search uses the new provider regardless of save timing.
- Provider menu open during listening: menu clicks never cancel recognition or start lasso; clicking elsewhere closes only the menu.
- Recognition completes while provider menu is open: show the result without discarding the provider selection; maintain deterministic z-order.
- Cancel races with Shazam response: cancellation wins; no late result card, ripple, Flow message, or URL action appears.
- Retry races with prior capture disposal: wait for the previous recognition task/capture lease to finish before starting a new one.
- Audio progress arrives after result/close: drop it via session generation ID or disposed flag.
- Silent audio: waveform remains in its baseline listening animation, emits no scene ripple, and final status remains `NoAudio`.
- Constant loud audio: normalized waveform remains capped; baseline adaptation and re-arm logic prevent a continuous ripple storm.
- Dense percussion: refractory interval and active-ripple cap bound allocations/render cost.
- Software renderer: do not add full-screen animated blur; reduce particles/ripples and use simple opacity/scale drawing.
- High Contrast/reduced motion: static waveform, no animated ripples/sparkles, immediate state transitions, readable controls.
- Negative-coordinate/mixed-DPI monitor: derive all element origins with `TransformToAncestor` in DIPs; keep capture bounds in physical pixels.
- Window deactivation and Flow/plugin disposal: cancel recognition, complete command channel, shut down dispatcher, and dispose frame exactly once.
- Copy failure/clipboard contention: keep the card open and show a localized failure or silently restore the copy icon after logging; do not crash the STA dispatcher.
- Unsafe/missing Shazam URL: render match data without an enabled link and never pass it to `Process.Start`.

## Tests

### Pure unit tests

Add tests that prove:

- provider-aware visual commands reject blank IDs and missing selections;
- selecting Yandex in the overlay updates the current visual command and calls persistence once;
- a persistence exception does not revert the current in-memory provider;
- a provider change during listening is accepted without canceling recognition;
- coordinator routes the current visual command's provider even when settings initially held the other provider;
- unknown saved provider falls back to Google Lens without constructing Yandex;
- audio level calculation is correct for float32 and PCM formats, multiple channels, silence, and clipped samples;
- normalization is monotonic and bounded;
- transient detector ignores silence/steady energy, fires on a significant rise, enforces refractory time, and re-arms;
- progress callbacks do not alter the existing 4/8/12 recognition attempt schedule or request serialization;
- cancellation/retry dispose each capture exactly once and suppress late progress/outcomes;
- ripple radius reaches the farthest corner for center, edge, and negative-DIP origins;
- scene ripple active-count cap and cleanup work deterministically;
- disabled animations create no active ripple timelines/render subscriptions.

### STA/UI tests

Update/add tests that prove:

- action tray contains selection chip, provider chip, and a separate music button in that order;
- provider menu excludes the selected provider and includes every other router descriptor;
- provider menu remains clickable during listening;
- listening blocks lasso mouse-down/move/up while provider/menu/music controls remain hit-testable;
- music click changes to listening instead of setting a terminal `MusicRecognition` outcome;
- screenshot/lasso layers fade while dim remains visible;
- match/no-match/no-audio cards expose correct automation names and actions;
- all render subscriptions and dispatcher work end when the overlay closes;
- existing negative-coordinate monitor and cancel tests remain green.

### Deterministic visual QA

Extend the existing preview harness so it can render fixed-time/fixed-level frames for:

- idle, provider menu open, Google selected, and Yandex selected;
- listening at silence, medium level, peak, and accepted transient;
- match pill, no-match card, and no-audio card;
- control ripple mid-expansion and scene ripple from waveform/result origins;
- dark and light themes at 100%, 150%, and 200% DPI;
- long localized/provider text without clipping.

Store generated artifacts under `tests/temp` through `TestOutputPaths`; do not write preview output into the repository root.

### Manual reproduction and neighboring cases

1. Start with Google, open the overlay, select Yandex, immediately draw a region, and confirm Yandex handles the current upload.
2. Reopen the overlay and confirm Yandex remains selected after settings reload.
3. Start listening, open the provider menu, switch back to Google, and confirm recognition/waveform continue uninterrupted.
4. While listening, attempt to draw a lasso and confirm no points are recorded.
5. Play audio with clear attacks and confirm waveform response is continuous while scene ripples occur only on strong onsets.
6. Play silence and constant steady audio and confirm no ripple storm occurs.
7. Confirm the desktop continues updating behind the dim layer after the frozen screenshot fades.
8. Test match at 4 seconds, later match at 8 seconds, no match at 12 seconds, no audio, rate limit, network failure, device failure, retry, and cancellation.
9. Confirm a music-match ripple begins at the result chip rather than screen center.
10. Confirm control ripples remain clipped inside provider/menu/music/result controls.
11. Confirm `Esc` closes an open provider menu first and cancels the session on the next press.
12. Confirm repeated hotkey cancellation produces no late result or ripple.

## Verification commands

Run from the repository root in PowerShell:

```powershell
dotnet build .\CircleToSearch.csproj -c Release
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
$env:CTS_CHIP_PREVIEW = '1'
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --filter ChipPreviewTests
Remove-Item Env:CTS_CHIP_PREVIEW
```

If new opt-in visual tests use separate environment variables, document and run each one. A green test result counts only after confirming that the expected non-zero number of tests executed. Also verify the original provider/music reproductions and neighboring cases above, not only the new isolated unit tests.

Optional live provider checks remain opt-in and must not be required for the ordinary suite:

```powershell
$env:CTS_LIVE = '1'
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --filter YandexLiveSearchTests
Remove-Item Env:CTS_LIVE
```

## Acceptance criteria

- The shipped overlay visually follows the three-element dock and provider menu in `design/chip-provider-music.html` in both themes.
- Google Lens and Yandex Images can be selected from the overlay without opening settings.
- A provider change applies to the current lasso selection and persists across plugin reload/restart.
- Provider selection remains usable during listening and never interrupts music recognition.
- Lasso input cannot run during listening or result display.
- The overlay remains open for the complete recognition/result interaction.
- The frozen screenshot fades away and the live desktop is visibly updating beneath the dim overlay.
- The existing five-dot waveform reacts smoothly to real Windows output level/peak data and remains stable under silence.
- Audio scene ripples originate at the waveform and occur only for accepted transients, with bounded frequency and active count.
- A successful music match emits one scene ripple from the result chip.
- Pointer ripples are clipped to the activated control and are independent of scene ripples.
- Existing 4/8/12 second recognition behavior, Shazam throttling, provider routing, URL safety, bitmap ownership, cancellation, and failure mappings remain correct.
- No fixed UI color is introduced outside `PluginPalette`, and no user-visible string is hardcoded in C#.
- `Main.cs` remains a thin Flow Launcher adapter and all runtime dependency assembly remains in `CompositionRoot.cs`.
- The full relevant test suite runs a confirmed non-zero number of tests and passes, followed by successful manual checks for both provider and music flows.

## Deferred work

- Replacing the five-dot waveform with a new circular/audio-aura design.
- Spectral-band or FFT-driven waveform deformation.
- True beat/BPM tracking; the initial detector is an onset/transient detector only.
- Returning from listening to lasso selection with a fresh desktop capture.
- Cover-art or additional music metadata requests.
