using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;

namespace CircleToSearch.Search;

internal sealed class OverlayMusicSession(
    IOverlaySession overlay,
    MusicRecognitionWorkflow workflow,
    MusicResultPresenter presenter,
    PluginLog log,
    CancellationToken sessionCancellation) : IOverlaySessionOperation
{
    private CancellationTokenSource? _cancellation;
    private Task<MusicRecognitionOutcome>? _pending;
    private MusicRecognitionOutcome? _displayedOutcome;
    private MusicDebugScenario _debugScenario = MusicDebugScenario.Live;

    public Task? PendingTask => _pending;
    public bool IsRunning => _pending is not null;

    public void SelectDebugScenario(MusicDebugScenario scenario)
    {
        _debugScenario = scenario;
        log.Info(nameof(OverlayMusicSession), $"music debug scenario changed to '{_debugScenario}'");
    }

    public async Task StartAsync()
    {
        if (_pending is not null) return;
        _displayedOutcome = null;
        await overlay.ShowListeningAsync(sessionCancellation).ConfigureAwait(false);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        _pending = workflow.RecognizeAsync(
            _debugScenario,
            new OverlayVisualizationProgress(overlay, _cancellation.Token, log),
            _cancellation.Token);
    }

    public async Task<OverlaySessionContinuation> OpenResultAsync()
    {
        if (_displayedOutcome?.Recognition is not { } match || !presenter.CanOpen(match))
            return OverlaySessionContinuation.Continue;
        await overlay.CloseAsync().ConfigureAwait(false);
        presenter.Open(match);
        return OverlaySessionContinuation.EndSession;
    }

    public void DismissResult() => _displayedOutcome = null;

    public async Task<OverlaySessionContinuation> CompletePendingAsync()
    {
        var pending = _pending ?? throw new InvalidOperationException("No music recognition is pending.");
        var outcome = await pending.ConfigureAwait(false);
        _pending = null;
        StopRecognition();
        if (sessionCancellation.IsCancellationRequested || outcome.Status == MusicRecognitionStatus.Canceled)
            return OverlaySessionContinuation.Continue;
        _displayedOutcome = outcome;
        try
        {
            await overlay.ShowMusicResultAsync(outcome, sessionCancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (!sessionCancellation.IsCancellationRequested)
        {
            log.Error(nameof(OverlayMusicSession), "showing the music result in the overlay failed", exception);
            presenter.PresentFallback(outcome);
            return OverlaySessionContinuation.EndSession;
        }
        return OverlaySessionContinuation.Continue;
    }

    public void RequestStop() => _cancellation?.Cancel();

    public async Task DrainAsync()
    {
        try
        {
            if (_pending is not null) await _pending.ConfigureAwait(false);
        }
        finally
        {
            _pending = null;
            DisposeCancellation();
        }
    }

    private void StopRecognition()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        finally { cancellation.Dispose(); }
    }

    private void DisposeCancellation()
    {
        _cancellation?.Dispose();
        _cancellation = null;
    }

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
                log.Warn(nameof(OverlayMusicSession),
                    $"forwarding audio visualization failed: {exception.Message}");
            }
        }
    }
}
