using System.Threading.Channels;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Settings;
using CircleToSearch.Ui;
using CircleToSearch.Translation;

namespace CircleToSearch.Search;

internal interface ISearchSessionWorkflow
{
    Task RunAsync(Action onUploadStarted, CancellationToken cancellationToken);
}

internal sealed class OverlaySessionWorkflow(
    IOverlaySessionFactory overlaySessionFactory,
    VisualSearchWorkflow visualSearch,
    MusicRecognitionWorkflow musicRecognition,
    MusicResultPresenter musicResultPresenter,
    ProviderSelectionStore providerSelection,
    PluginSettings settings,
    UiStrings strings,
    PluginLog log,
    TextSearchWorkflow? textSearch = null,
    ScreenTranslationWorkflow? screenTranslation = null,
    Func<string, bool>? openTraceUrl = null) : ISearchSessionWorkflow
{
    public async Task RunAsync(Action onUploadStarted, CancellationToken cancellationToken)
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
        CancellationTokenSource? translationCancellation = null;
        Task<ScreenTranslationOutcome>? translationTask = null;
        Guid translationRequestId = Guid.Empty;
        using var traceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<VisualSearchPreparationOutcome>? traceTask = null;
        TraceMoeMatch? traceMatch = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                commandTask ??= overlay.ReadCommandAsync(cancellationToken);
                var pending = new List<Task> { commandTask };
                if (recognitionTask is not null) pending.Add(recognitionTask);
                if (translationTask is not null) pending.Add(translationTask);
                if (traceTask is not null) pending.Add(traceTask);
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                if (traceTask is not null && ReferenceEquals(completed, traceTask))
                {
                    var outcome = await traceTask.ConfigureAwait(false);
                    traceTask = null;
                    traceMatch = outcome.PreparedSearch?.TraceMatch;
                    if (!cancellationToken.IsCancellationRequested && outcome.Failure != UploadFailure.Canceled)
                        await overlay.ShowTraceResultAsync(outcome, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (translationTask is not null && ReferenceEquals(completed, translationTask))
                {
                    var outcome = await translationTask.ConfigureAwait(false);
                    var completedRequestId = translationRequestId;
                    translationTask = null;
                    translationRequestId = Guid.Empty;
                    translationCancellation?.Dispose();
                    translationCancellation = null;
                    if (cancellationToken.IsCancellationRequested || outcome.Failure == TranslationFailure.Canceled)
                        continue;
                    if (outcome.Result is { } result)
                        await overlay.ShowTranslationAsync(result, cancellationToken).ConfigureAwait(false);
                    else
                        await overlay.ShowTranslationFailureAsync(completedRequestId, outcome.Failure, cancellationToken)
                            .ConfigureAwait(false);
                    continue;
                }
                if (recognitionTask is not null)
                {
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

                    case VisualSelection visual when visual.ProviderId == SearchProviderIds.TraceMoe && traceTask is null:
                        traceTask = visualSearch.PrepareTraceAsync(visual.Selection, traceCancellation.Token);
                        break;

                    case OpenTraceResult:
                        if (traceMatch is not null)
                        {
                            await overlay.CloseAsync().ConfigureAwait(false);
                            openTraceUrl?.Invoke(traceMatch.AnilistUrl);
                            return;
                        }
                        break;

                    case VisualSelection visual when recognitionTask is null:
                        try
                        {
                            // Selection is published before its topmost confirmation overlay finishes closing.
                            await overlay.WaitForCloseAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            visual.Selection.FrozenFrame.Dispose();
                            throw;
                        }
                        await visualSearch.ExecuteAsync(
                            visual.Selection,
                            visual.ProviderId,
                            onUploadStarted,
                            cancellationToken).ConfigureAwait(false);
                        return;

                    case SearchSelectedText selectedText when textSearch is not null:
                        await overlay.CloseAsync().ConfigureAwait(false);
                        textSearch.Execute(selectedText.Text, selectedText.ProviderId);
                        return;

                    case ScreenTranslationRequested requested when
                        screenTranslation is not null && translationTask is null:
                        translationRequestId = requested.RequestId;
                        translationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        translationTask = screenTranslation.TranslateAsync(
                            requested.RequestId,
                            requested.Document,
                            requested.TargetLanguageTag,
                            translationCancellation.Token);
                        break;

                    case CancelScreenTranslation canceled when
                        translationTask is not null && canceled.RequestId == translationRequestId:
                        translationCancellation?.Cancel();
                        try { await translationTask.ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                        translationTask = null;
                        translationRequestId = Guid.Empty;
                        translationCancellation?.Dispose();
                        translationCancellation = null;
                        break;

                    case StartMusicRecognition when recognitionTask is null:
                    case RetryMusicRecognition when recognitionTask is null:
                        displayedOutcome = null;
                        await overlay.ShowListeningAsync(cancellationToken).ConfigureAwait(false);
                        recognitionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
            traceCancellation.Cancel();
            if (traceTask is not null)
            {
                try { await traceTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
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
            if (translationCancellation is not null)
            {
                translationCancellation.Cancel();
                if (translationTask is not null)
                {
                    try { await translationTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                translationCancellation.Dispose();
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
