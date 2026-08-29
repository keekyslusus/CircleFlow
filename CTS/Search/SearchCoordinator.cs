using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Settings;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;
using System.Threading.Channels;

namespace CircleToSearch.Search;

public enum SearchState { Idle, Selecting, Uploading, RecognizingMusic, ShowingMusicResult }

public sealed class SearchCoordinator
{
    private readonly VisualSearchProviderRouter _providerRouter;
    private readonly Func<CancellationToken, Task<OverlayOutcome?>> _selection;
    private readonly IOverlaySessionFactory? _overlaySessionFactory;
    private readonly Action? _saveSettings;
    private readonly Func<GdiBitmap, GdiRectangle, byte[]> _crop;
    private readonly IMusicRecognizer _musicRecognizer;
    private readonly IMusicRecognitionSimulator? _musicSimulator;
    private readonly Func<string, bool> _openUrl;
    private readonly Action _hideMainWindow;
    private readonly Action<string, string> _showMessage;
    private readonly Action<string, string, string, Action> _showMessageWithButton;
    private readonly Action<string, string> _showError;
    private readonly PluginSettings _settings;
    private readonly UiStrings _strings;
    private readonly PluginLog _log;
    private readonly SemaphoreSlim _session = new(1, 1);
    private int _state;
    private CancellationTokenSource? _cancellation;
    private Task _activeSession = Task.CompletedTask;

    public SearchCoordinator(
        VisualSearchProviderRouter providerRouter,
        Func<CancellationToken, Task<OverlayOutcome?>> selection,
        Func<GdiBitmap, GdiRectangle, byte[]> crop,
        IMusicRecognizer musicRecognizer,
        Func<string, bool> openUrl,
        Action hideMainWindow,
        Action<string, string> showMessage,
        Action<string, string, string, Action> showMessageWithButton,
        Action<string, string> showError,
        PluginSettings settings,
        UiStrings strings,
        PluginLog log)
    {
        _providerRouter = providerRouter;
        _selection = selection;
        _crop = crop;
        _musicRecognizer = musicRecognizer;
        _openUrl = openUrl;
        _hideMainWindow = hideMainWindow;
        _showMessage = showMessage;
        _showMessageWithButton = showMessageWithButton;
        _showError = showError;
        _settings = settings;
        _strings = strings;
        _log = log;
    }

    public SearchCoordinator(
        VisualSearchProviderRouter providerRouter,
        IOverlaySessionFactory overlaySessionFactory,
        Func<GdiBitmap, GdiRectangle, byte[]> crop,
        IMusicRecognizer musicRecognizer,
        IMusicRecognitionSimulator musicSimulator,
        Func<string, bool> openUrl,
        Action hideMainWindow,
        Action<string, string> showMessage,
        Action<string, string, string, Action> showMessageWithButton,
        Action<string, string> showError,
        Action saveSettings,
        PluginSettings settings,
        UiStrings strings,
        PluginLog log)
        : this(
            providerRouter,
            _ => Task.FromResult<OverlayOutcome?>(null),
            crop,
            musicRecognizer,
            openUrl,
            hideMainWindow,
            showMessage,
            showMessageWithButton,
            showError,
            settings,
            strings,
            log)
    {
        _overlaySessionFactory = overlaySessionFactory ?? throw new ArgumentNullException(nameof(overlaySessionFactory));
        _musicSimulator = musicSimulator ?? throw new ArgumentNullException(nameof(musicSimulator));
        _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
    }

    public SearchState State => (SearchState)Volatile.Read(ref _state);

    public Task StartFromHotkeyAsync()
    {
        try
        {
            return State switch
            {
                SearchState.Selecting or SearchState.RecognizingMusic or SearchState.ShowingMusicResult => CancelActiveSession(),
                SearchState.Uploading => IgnoreTrigger("upload in progress"),
                _ => StartSession("hotkey"),
            };
        }
        catch (Exception exception)
        {
            SurfaceFailure("starting the selection failed", _strings.StartingSelectionFailed(exception.Message), exception);
            return Task.CompletedTask;
        }
    }

    public Task StartFromQueryAsync()
    {
        try
        {
            return State == SearchState.Idle ? StartSession("query") : IgnoreTrigger("another session is active");
        }
        catch (Exception exception)
        {
            SurfaceFailure("starting the selection failed", _strings.StartingSelectionFailed(exception.Message), exception);
            return Task.CompletedTask;
        }
    }

    public Task CancelActiveSession()
    {
        try
        {
            _log.Info(nameof(SearchCoordinator), "canceling the active session");
            Volatile.Read(ref _cancellation)?.Cancel();
        }
        catch (Exception exception)
        {
            _log.Error(nameof(SearchCoordinator), "canceling the session failed", exception);
        }
        return Volatile.Read(ref _activeSession);
    }

    private Task StartSession(string trigger)
    {
        var session = RunSessionAsync(trigger);
        Volatile.Write(ref _activeSession, session);
        return session;
    }

    private async Task RunSessionAsync(string trigger)
    {
        if (!await _session.WaitAsync(0).ConfigureAwait(false))
        {
            _log.Info(nameof(SearchCoordinator), $"trigger '{trigger}' ignored: session already active");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        Volatile.Write(ref _cancellation, cancellation);
        var requestedProviderId = _settings.SearchProviderId;
        try
        {
            SetState(SearchState.Selecting);
            _log.Info(nameof(SearchCoordinator), $"selection started via {trigger}");
            SafeHideMainWindow();
            try
            {
                await Task.Delay(_settings.HideDelayMilliseconds, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_overlaySessionFactory is not null)
            {
                await RunOverlaySessionAsync(_overlaySessionFactory, cancellation.Token).ConfigureAwait(false);
                return;
            }

            var outcome = await _selection(cancellation.Token).ConfigureAwait(false);
            if (outcome is null)
            {
                _log.Info(nameof(SearchCoordinator), "selection canceled");
                return;
            }
            if (outcome.Action == OverlayAction.MusicRecognition)
            {
                await RunMusicRecognitionAsync(cancellation.Token).ConfigureAwait(false);
                return;
            }
            await RunVisualSearchAsync(
                outcome.Selection ?? throw new InvalidOperationException("Visual outcome has no selection."),
                requestedProviderId,
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Info(nameof(SearchCoordinator), "session canceled");
        }
        catch (Exception exception)
        {
            SurfaceFailure("the search failed", _strings.SearchFailed(exception.Message), exception);
        }
        finally
        {
            SetState(SearchState.Idle);
            Volatile.Write(ref _cancellation, null);
            _session.Release();
        }
    }

    private async Task RunOverlaySessionAsync(
        IOverlaySessionFactory factory,
        CancellationToken cancellationToken)
    {
        var effective = _providerRouter.GetEffectiveDescriptor(_settings.SearchProviderId);
        var launch = new OverlayLaunchOptions(
            new OverlayOptions(_settings.PaddingPx, _settings.LassoMinDiagonalPx),
            _strings,
            _providerRouter.Providers,
            effective.Id);
        await using var overlay = await factory.OpenAsync(launch, cancellationToken).ConfigureAwait(false);
        if (overlay is null) return;

        CancellationTokenSource? recognitionCancellation = null;
        Task<MusicRecognitionOutcome>? recognitionTask = null;
        MusicRecognitionOutcome? displayedOutcome = null;
        var debugScenario = MusicDebugScenario.Live;
        Task<IOverlayCommand>? commandTask = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                commandTask ??= overlay.ReadCommandAsync(cancellationToken);
                if (recognitionTask is not null)
                {
                    var completed = await Task.WhenAny(commandTask, recognitionTask).ConfigureAwait(false);
                    if (ReferenceEquals(completed, recognitionTask))
                    {
                        var outcome = await recognitionTask.ConfigureAwait(false);
                        recognitionTask = null;
                        recognitionCancellation?.Cancel();
                        recognitionCancellation?.Dispose();
                        recognitionCancellation = null;
                        if (cancellationToken.IsCancellationRequested || outcome.Status == MusicRecognitionStatus.Canceled)
                            continue;
                        displayedOutcome = outcome;
                        SetState(SearchState.ShowingMusicResult);
                        try
                        {
                            await overlay.ShowMusicResultAsync(outcome, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                        {
                            _log.Error(nameof(SearchCoordinator), "showing the music result in the overlay failed", exception);
                            PresentMusicOutcomeFallback(outcome);
                            return;
                        }
                        continue;
                    }
                }

                var command = await commandTask.ConfigureAwait(false);
                commandTask = null;
                switch (command)
                {
                    case ProviderSelected provider:
                        PersistProviderSelection(provider.ProviderId);
                        break;

                    case MusicDebugScenarioSelected selected:
                        debugScenario = selected.Scenario;
                        _log.Info(nameof(SearchCoordinator), $"music debug scenario changed to '{debugScenario}'");
                        break;

                    case VisualSelection visual when recognitionTask is null:
                        await RunVisualSearchAsync(
                            visual.Selection,
                            visual.ProviderId,
                            cancellationToken).ConfigureAwait(false);
                        return;

                    case StartMusicRecognition when recognitionTask is null:
                        displayedOutcome = null;
                        await overlay.ShowListeningAsync(cancellationToken).ConfigureAwait(false);
                        recognitionTask = StartRecognitionAsync(
                            overlay,
                            debugScenario,
                            cancellationToken,
                            out recognitionCancellation);
                        break;

                    case RetryMusicRecognition when recognitionTask is null:
                        displayedOutcome = null;
                        await overlay.ShowListeningAsync(cancellationToken).ConfigureAwait(false);
                        recognitionTask = StartRecognitionAsync(
                            overlay,
                            debugScenario,
                            cancellationToken,
                            out recognitionCancellation);
                        break;

                    case OpenMusicResult:
                        if (displayedOutcome?.Recognition is { } match && IsSafeShazamUrl(match.ShazamUrl))
                        {
                            await overlay.CloseAsync().ConfigureAwait(false);
                            if (!_openUrl(match.ShazamUrl!)) SurfaceError(_strings.PluginTitle, _strings.ResultsUrlOpenFailed);
                            return;
                        }
                        break;

                    case CopyMusicResult:
                        break;

                    case DismissMusicResult:
                        displayedOutcome = null;
                        SetState(SearchState.Selecting);
                        break;

                    case CancelSession:
                        return;
                }
            }
        }
        catch (ChannelClosedException)
        {
            _log.Info(nameof(SearchCoordinator), "overlay command channel closed");
        }
        finally
        {
            if (recognitionCancellation is not null)
            {
                recognitionCancellation.Cancel();
                if (recognitionTask is not null)
                {
                    try { await recognitionTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                recognitionCancellation.Dispose();
            }
            await overlay.CloseAsync().ConfigureAwait(false);
        }
    }

    private Task<MusicRecognitionOutcome> StartRecognitionAsync(
        IOverlaySession overlay,
        MusicDebugScenario debugScenario,
        CancellationToken sessionCancellation,
        out CancellationTokenSource recognitionCancellation)
    {
        recognitionCancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        SetState(SearchState.RecognizingMusic);
        _log.Info(nameof(SearchCoordinator), debugScenario == MusicDebugScenario.Live
            ? "music recognition started"
            : $"simulated music recognition started with '{debugScenario}'");
        var progress = new OverlayVisualizationProgress(overlay, recognitionCancellation.Token, _log);
        return debugScenario == MusicDebugScenario.Live
            ? _musicRecognizer.RecognizeAsync(progress, recognitionCancellation.Token)
            : (_musicSimulator ?? throw new InvalidOperationException("The music simulator is not configured."))
                .RecognizeAsync(debugScenario, progress, recognitionCancellation.Token);
    }

    private void PersistProviderSelection(string requestedProviderId)
    {
        var selected = _providerRouter.GetEffectiveDescriptor(requestedProviderId);
        _settings.SearchProviderId = selected.Id;
        try
        {
            _saveSettings?.Invoke();
            _log.Info(nameof(SearchCoordinator), $"visual search provider changed to '{selected.Id}'");
        }
        catch (Exception exception)
        {
            _log.Error(nameof(SearchCoordinator), "saving the visual search provider failed", exception);
            SurfaceError(_strings.PluginTitle, _strings.SavingFailed(exception.Message));
        }
    }

    private void PresentMusicOutcomeFallback(MusicRecognitionOutcome outcome)
    {
        switch (outcome.Status)
        {
            case MusicRecognitionStatus.Matched when outcome.Recognition is { } match:
                ShowMusicMatch(match);
                break;
            case MusicRecognitionStatus.NoMatch:
                SurfaceMessage(_strings.PluginTitle, _strings.MusicNoMatch);
                break;
            case MusicRecognitionStatus.NoAudio:
                SurfaceMessage(_strings.PluginTitle, _strings.MusicNoAudio);
                break;
            case MusicRecognitionStatus.RateLimited:
                SurfaceError(_strings.PluginTitle, _strings.MusicRateLimited);
                break;
            case MusicRecognitionStatus.DeviceError:
                SurfaceError(_strings.PluginTitle, _strings.MusicDeviceError);
                break;
            case MusicRecognitionStatus.ServiceError:
                SurfaceError(_strings.PluginTitle, _strings.MusicNetworkError);
                break;
        }
    }

    private async Task RunVisualSearchAsync(
        SelectionOutcome outcome,
        string requestedProviderId,
        CancellationToken cancellationToken)
    {
        byte[] png;
        try { png = _crop(outcome.FrozenFrame, outcome.Bounds); }
        finally { outcome.FrozenFrame.Dispose(); }

        SetState(SearchState.Uploading);
        var selectedProvider = _providerRouter.GetEffectiveDescriptor(requestedProviderId);
        _log.Info(nameof(SearchCoordinator), $"upload started with provider '{selectedProvider.Id}'");
        var routed = await _providerRouter.SearchAsync(requestedProviderId, png, cancellationToken).ConfigureAwait(false);
        var result = routed.Outcome;
        _log.Info(nameof(SearchCoordinator), $"provider '{routed.ProviderId}' completed with {result.Failure}");
        if (!result.Success)
        {
            var reason = result.Failure switch
            {
                UploadFailure.UnexpectedStatus => _strings.SearchUnexpectedStatus(result.StatusCode),
                UploadFailure.BadResponse => _strings.SearchUnexpectedResponse,
                UploadFailure.PolicyRejection => _strings.SearchUnexpectedResultsLocation,
                UploadFailure.Timeout => _strings.SearchTimedOut,
                UploadFailure.NetworkError => _strings.SearchNetworkError,
                UploadFailure.BrowserRuntimeUnavailable => _strings.BrowserRuntimeRequired(routed.ProviderDisplayName),
                UploadFailure.BrowserAutomationFailed => _strings.BrowserImageAttachmentFailed(routed.ProviderDisplayName),
                UploadFailure.Canceled => null,
                _ => _strings.SearchUploadFailed,
            };
            if (reason is null) return;
            _log.Warn(nameof(SearchCoordinator), $"provider '{routed.ProviderId}' failed: {result.Failure} status {result.StatusCode}");
            SurfaceError(_strings.PluginTitle, reason);
            return;
        }

        if (result.ResultsUrl is { Length: > 0 } url)
        {
            if (!_openUrl(url)) SurfaceError(_strings.PluginTitle, _strings.ResultsUrlOpenFailed);
            else _log.Info(nameof(SearchCoordinator), $"provider '{routed.ProviderId}' results opened in the default browser");
        }
        else _log.Info(nameof(SearchCoordinator), $"results delivered by provider '{routed.ProviderId}'");
    }

    private async Task RunMusicRecognitionAsync(CancellationToken cancellationToken)
    {
        SetState(SearchState.RecognizingMusic);
        _log.Info(nameof(SearchCoordinator), "music recognition started");
        var outcome = await _musicRecognizer.RecognizeAsync(cancellationToken).ConfigureAwait(false);
        _log.Info(nameof(SearchCoordinator), $"music recognition completed with {outcome.Status}");
        switch (outcome.Status)
        {
            case MusicRecognitionStatus.Matched when outcome.Recognition is { } match:
                ShowMusicMatch(match);
                break;
            case MusicRecognitionStatus.NoMatch:
                SurfaceMessage(_strings.PluginTitle, _strings.MusicNoMatch);
                break;
            case MusicRecognitionStatus.NoAudio:
                SurfaceMessage(_strings.PluginTitle, _strings.MusicNoAudio);
                break;
            case MusicRecognitionStatus.RateLimited:
                SurfaceError(_strings.PluginTitle, _strings.MusicRateLimited);
                break;
            case MusicRecognitionStatus.DeviceError:
                SurfaceError(_strings.PluginTitle, _strings.MusicDeviceError);
                break;
            case MusicRecognitionStatus.ServiceError:
                SurfaceError(_strings.PluginTitle, _strings.MusicNetworkError);
                break;
        }
    }

    private void ShowMusicMatch(ShazamRecognition match)
    {
        var title = $"{match.Artist} — {match.Title}";
        var subtitle = _strings.MusicMatchSubtitle(match.Album, match.Genre);
        if (IsSafeShazamUrl(match.ShazamUrl))
        {
            SurfaceMessageWithButton(title, subtitle, _strings.OpenInShazam, () =>
            {
                if (!_openUrl(match.ShazamUrl!)) SurfaceError(_strings.PluginTitle, _strings.ResultsUrlOpenFailed);
            });
        }
        else SurfaceMessage(title, subtitle);
    }

    internal static bool IsSafeShazamUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(parsed.Host, "shazam.com", StringComparison.OrdinalIgnoreCase) ||
               parsed.Host.EndsWith(".shazam.com", StringComparison.OrdinalIgnoreCase);
    }

    private Task IgnoreTrigger(string reason)
    {
        _log.Info(nameof(SearchCoordinator), $"hotkey ignored: {reason}");
        return Task.CompletedTask;
    }

    private void SafeHideMainWindow()
    {
        try { _hideMainWindow(); }
        catch (Exception exception) { _log.Warn(nameof(SearchCoordinator), $"hiding the Flow window failed: {exception.Message}"); }
    }

    private void SurfaceMessage(string title, string message)
    {
        try { _showMessage(title, message); }
        catch (Exception exception) { _log.Error(nameof(SearchCoordinator), "showing the message failed", exception); }
    }

    private void SurfaceMessageWithButton(string title, string message, string button, Action action)
    {
        try { _showMessageWithButton(title, message, button, action); }
        catch (Exception exception) { _log.Error(nameof(SearchCoordinator), "showing the message with button failed", exception); }
    }

    private void SurfaceError(string title, string message)
    {
        try { _showError(title, message); }
        catch (Exception exception) { _log.Error(nameof(SearchCoordinator), "showing the error message failed", exception); }
    }

    private void SurfaceFailure(string logMessage, string userMessage, Exception exception)
    {
        _log.Error(nameof(SearchCoordinator), logMessage, exception);
        SurfaceError(_strings.PluginTitle, userMessage);
    }

    private void SetState(SearchState state) => Volatile.Write(ref _state, (int)state);

    private sealed class OverlayVisualizationProgress(
        IOverlaySession overlay,
        CancellationToken cancellationToken,
        PluginLog log) : IMusicVisualizationProgress
    {
        public void Report(MusicVisualizationFrame frame)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try { _ = overlay.ReportAudioAsync(frame, cancellationToken); }
            catch (Exception exception)
            {
                log.Warn(nameof(SearchCoordinator), $"forwarding audio visualization failed: {exception.Message}");
            }
        }
    }
}
