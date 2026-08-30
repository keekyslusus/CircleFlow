using System.Threading.Channels;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Settings;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal interface ISearchSessionWorkflow
{
    Task RunAsync(Action<SearchState> transition, CancellationToken cancellationToken);
}

internal sealed class OverlaySessionWorkflow(
    IOverlaySessionFactory overlaySessionFactory,
    VisualSearchWorkflow visualSearch,
    MusicRecognitionWorkflow musicRecognition,
    MusicResultPresenter musicResultPresenter,
    ProviderSelectionStore providerSelection,
    PluginSettings settings,
    UiStrings strings,
    PluginLog log) : ISearchSessionWorkflow
{
    public async Task RunAsync(Action<SearchState> transition, CancellationToken cancellationToken)
    {
        var effective = providerSelection.GetEffectiveSelection();
        var launch = new OverlayLaunchOptions(
            new OverlayOptions(settings.PaddingPx, settings.LassoMinDiagonalPx),
            strings,
            providerSelection.Providers,
            effective.Id);
        await using var overlay = await overlaySessionFactory.OpenAsync(launch, cancellationToken).ConfigureAwait(false);
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
                        transition(SearchState.ShowingMusicResult);
                        try
                        {
                            await overlay.ShowMusicResultAsync(outcome, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                        {
                            log.Error(nameof(OverlaySessionWorkflow),
                                "showing the music result in the overlay failed", exception);
                            musicResultPresenter.PresentFallback(outcome);
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
                        providerSelection.Save(provider.ProviderId);
                        break;

                    case MusicDebugScenarioSelected selected:
                        debugScenario = selected.Scenario;
                        log.Info(nameof(OverlaySessionWorkflow),
                            $"music debug scenario changed to '{debugScenario}'");
                        break;

                    case VisualSelection visual when recognitionTask is null:
                        await visualSearch.ExecuteAsync(
                            visual.Selection,
                            visual.ProviderId,
                            () => transition(SearchState.Uploading),
                            cancellationToken).ConfigureAwait(false);
                        return;

                    case StartMusicRecognition when recognitionTask is null:
                    case RetryMusicRecognition when recognitionTask is null:
                        displayedOutcome = null;
                        await overlay.ShowListeningAsync(cancellationToken).ConfigureAwait(false);
                        recognitionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        transition(SearchState.RecognizingMusic);
                        recognitionTask = musicRecognition.RecognizeAsync(
                            debugScenario,
                            new OverlayVisualizationProgress(overlay, recognitionCancellation.Token, log),
                            recognitionCancellation.Token);
                        break;

                    case OpenMusicResult:
                        if (displayedOutcome?.Recognition is { } match && musicResultPresenter.CanOpen(match))
                        {
                            await overlay.CloseAsync().ConfigureAwait(false);
                            musicResultPresenter.Open(match);
                            return;
                        }
                        break;

                    case CopyMusicResult:
                        break;

                    case DismissMusicResult:
                        displayedOutcome = null;
                        transition(SearchState.Selecting);
                        break;

                    case CancelSession:
                        return;
                }
            }
        }
        catch (ChannelClosedException)
        {
            log.Info(nameof(OverlaySessionWorkflow), "overlay command channel closed");
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
                log.Warn(nameof(OverlaySessionWorkflow),
                    $"forwarding audio visualization failed: {exception.Message}");
            }
        }
    }
}
