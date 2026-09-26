using System.Threading.Channels;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using CircleToSearch.Translation;

namespace CircleToSearch.Search;

internal interface ISearchSessionWorkflow
{
    Task RunAsync(SearchSessionOptions options, Action onUploadStarted, CancellationToken cancellationToken);
}

internal sealed class OverlaySessionWorkflow(
    IOverlaySessionFactory overlaySessionFactory,
    VisualSearchWorkflow visualSearch,
    Func<IOverlaySession, CancellationToken, OverlayMusicSession> createMusicSession,
    Func<IOverlaySession, CancellationToken, OverlayTranslationSession>? createTranslationSession,
    Func<IOverlaySession, int, CancellationToken, OverlayTraceSession> createTraceSession,
    ProviderSelectionStore providerSelection,
    UiStrings strings,
    PluginLog log,
    TextSearchWorkflow? textSearch = null,
    Func<System.Windows.Media.Imaging.BitmapSource, Task>? saveImage = null,
    Func<int, CancellationToken, OverlayAskSession>? createAskSession = null,
    Func<int, CancellationToken, OverlayLensSession>? createLensSession = null,
    Func<CancellationToken, OverlayTextSearchSession>? createTextSearchSession = null) : ISearchSessionWorkflow
{
    public async Task RunAsync(SearchSessionOptions options, Action onUploadStarted, CancellationToken cancellationToken)
    {
        var effective = providerSelection.GetEffectiveSelection();
        var launch = new OverlayLaunchOptions(
            new OverlayOptions(options.PaddingPx, options.LassoMinDiagonalPx),
            strings,
            providerSelection.Providers,
            effective.Id,
            options);
        var overlay = await overlaySessionFactory.OpenAsync(launch, cancellationToken).ConfigureAwait(false);
        if (overlay is null) return;

        using var commandReadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operations = new List<IOverlaySessionOperation>(3);
        OverlayMusicSession? music = null;
        OverlayTranslationSession? translation = null;
        OverlayTraceSession? trace = null;
        OverlayAskSession? ask = null;
        OverlayLensSession? lens = null;
        OverlayTextSearchSession? textWarm = null;
        Task<IOverlayCommand>? commandTask = null;
        try
        {
            music = createMusicSession(overlay, cancellationToken);
            operations.Add(music);
            ask = createAskSession?.Invoke(options.MaxLongSidePx, cancellationToken);
            if (ask is not null) operations.Add(ask);
            lens = createLensSession?.Invoke(options.MaxLongSidePx, cancellationToken);
            if (lens is not null) operations.Add(lens);
            textWarm = createTextSearchSession?.Invoke(cancellationToken);
            if (textWarm is not null) operations.Add(textWarm);
            translation = createTranslationSession?.Invoke(overlay, cancellationToken);
            if (translation is not null) operations.Add(translation);
            trace = createTraceSession(overlay, options.MaxLongSidePx, cancellationToken);
            operations.Add(trace);

            while (!cancellationToken.IsCancellationRequested)
            {
                commandTask ??= overlay.ReadCommandAsync(commandReadCancellation.Token);
                var pending = new List<Task>(operations.Count + 1) { commandTask };
                foreach (var operation in operations)
                    if (operation.PendingTask is { } task) pending.Add(task);
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                var completedOperation = operations.FirstOrDefault(
                    operation => ReferenceEquals(operation.PendingTask, completed));
                if (completedOperation is not null)
                {
                    if (await completedOperation.CompletePendingAsync().ConfigureAwait(false) ==
                        OverlaySessionContinuation.EndSession)
                        return;
                    continue;
                }

                var command = await commandTask.ConfigureAwait(false);
                commandTask = null;
                var commandOwnershipTransferred = false;
                try
                {
                    switch (command)
                    {
                        case SaveSelectedImage saved when saveImage is not null:
                            await overlay.CloseAsync().ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            await saveImage(saved.Image).ConfigureAwait(false);
                            return;

                        case ProviderSelected provider:
                            providerSelection.Save(provider.ProviderId);
                            if (!OverlayLensSession.Handles(provider.ProviderId))
                                await ReleaseWarmLensAsync(lens).ConfigureAwait(false);
                            // The warm browser is named after the previous provider's search site.
                            await ReleaseWarmTextSearchAsync(textWarm).ConfigureAwait(false);
                            break;

                        case MusicDebugScenarioSelected selected:
                            music.SelectDebugScenario(selected.Scenario);
                            break;

                        case VisualSelection visual when trace.CanStart(visual):
                            trace.Start(visual);
                            commandOwnershipTransferred = true;
                            break;

                        case OpenTraceResult:
                            if (await trace.OpenResultAsync().ConfigureAwait(false) ==
                                OverlaySessionContinuation.EndSession)
                                return;
                            break;

                        case VisualSelectionStarted started when lens is not null && ask?.PendingTask is null &&
                            !music.IsRunning && OverlayLensSession.Handles(started.ProviderId):
                            await ReleaseWarmTextSearchAsync(textWarm).ConfigureAwait(false);
                            lens.Start();
                            break;

                        case TextSelectionStarted started when textWarm is not null &&
                            textSearch is { OpensInBuiltInBrowser: true } && ask?.PendingTask is null &&
                            !music.IsRunning:
                            await ReleaseWarmLensAsync(lens).ConfigureAwait(false);
                            textWarm.Start(started.ProviderId);
                            break;

                        case VisualSelection visual when lens is { IsWarming: true } && !music.IsRunning &&
                            OverlayLensSession.Handles(visual.ProviderId):
                            var searching = lens.SubmitAsync(
                                visual.Selection,
                                overlay.WaitForCloseAsync(cancellationToken),
                                onUploadStarted);
                            commandOwnershipTransferred = true;
                            await searching.ConfigureAwait(false);
                            return;

                        case VisualSelection visual when !music.IsRunning:
                            // Selection is published before its topmost confirmation overlay finishes closing.
                            await overlay.WaitForCloseAsync(cancellationToken).ConfigureAwait(false);
                            var execution = visualSearch.ExecuteAsync(
                                visual.Selection,
                                visual.ProviderId,
                                onUploadStarted,
                                options.MaxLongSidePx,
                                cancellationToken);
                            commandOwnershipTransferred = true;
                            await execution.ConfigureAwait(false);
                            return;

                        case AskDraftStarted when ask is not null && !music.IsRunning:
                            ask.Start();
                            break;

                        case AskImageAttached attached when ask is not null:
                            commandOwnershipTransferred = true;
                            ask.AttachImage(attached.Selection);
                            break;

                        case AskDraftCanceled when ask is not null:
                            await ask.CancelAsync().ConfigureAwait(false);
                            break;

                        case AskAboutSelection asked when ask is not null && !music.IsRunning:
                            await overlay.CloseAsync().ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            var asking = ask.SubmitAsync(asked, onUploadStarted);
                            commandOwnershipTransferred = true;
                            await asking.ConfigureAwait(false);
                            return;

                        case SearchSelectedText selectedText when textWarm is { IsWarming: true }:
                            await textWarm.SubmitAsync(selectedText, overlay.CloseAsync, onUploadStarted)
                                .ConfigureAwait(false);
                            return;

                        case SearchSelectedText selectedText when textSearch is not null:
                            await overlay.CloseAsync().ConfigureAwait(false);
                            await ReleaseWarmLensAsync(lens).ConfigureAwait(false);
                            await textSearch.ExecuteAsync(
                                selectedText.Text,
                                selectedText.ProviderId,
                                onUploadStarted,
                                cancellationToken).ConfigureAwait(false);
                            return;

                        case ScreenTranslationRequested requested when
                            translation is not null && !translation.IsRunning:
                            translation.Start(requested);
                            break;

                        case CancelScreenTranslation canceled when translation is not null:
                            await translation.CancelAsync(canceled).ConfigureAwait(false);
                            break;

                        case StartMusicRecognition when !music.IsRunning:
                        case RetryMusicRecognition when !music.IsRunning:
                            await ReleaseWarmLensAsync(lens).ConfigureAwait(false);
                            await ReleaseWarmTextSearchAsync(textWarm).ConfigureAwait(false);
                            await music.StartAsync().ConfigureAwait(false);
                            break;

                        case OpenMusicResult:
                            if (await music.OpenResultAsync().ConfigureAwait(false) ==
                                OverlaySessionContinuation.EndSession)
                                return;
                            break;

                        case DismissMusicResult:
                            music.DismissResult();
                            break;

                        case CancelSession:
                            return;
                    }
                }
                finally
                {
                    if (!commandOwnershipTransferred)
                        OverlayCommandOwnership.DisposePayload(command);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (ChannelClosedException)
        {
            log.Info(nameof(OverlaySessionWorkflow), "overlay command channel closed");
        }
        finally
        {
            TryCleanup(() => commandReadCancellation.Cancel(), "cancel-command-read");
            foreach (var operation in operations)
                TryCleanup(operation.RequestStop, "stop-session-operation");

            var cleanup = new List<Task>(operations.Count + 2)
            {
                CloseOverlayAsync(overlay),
                commandTask is null ? Task.CompletedTask : ObservePendingCommandAsync(commandTask),
            };
            cleanup.AddRange(operations.Select(DrainOperationAsync));
            await Task.WhenAll(cleanup).ConfigureAwait(false);
            await DisposeOverlayAsync(overlay).ConfigureAwait(false);
        }
    }

    // A warm Lens browser only pays off while the next lasso may still search with Lens.
    private static Task ReleaseWarmLensAsync(OverlayLensSession? lens) =>
        lens?.CancelAsync() ?? Task.CompletedTask;

    private static Task ReleaseWarmTextSearchAsync(OverlayTextSearchSession? textWarm) =>
        textWarm?.CancelAsync() ?? Task.CompletedTask;

    private void TryCleanup(Action action, string operation)
    {
        try { action(); }
        catch (Exception exception)
        {
            log.SafeError(nameof(OverlaySessionWorkflow), operation, exception);
        }
    }

    private async Task ObservePendingCommandAsync(Task<IOverlayCommand> commandTask)
    {
        try
        {
            var command = await commandTask.ConfigureAwait(false);
            OverlayCommandOwnership.DisposePayload(command);
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }
        catch (Exception exception)
        {
            log.SafeError(nameof(OverlaySessionWorkflow), "complete-command-read", exception);
        }
    }

    private async Task DrainOperationAsync(IOverlaySessionOperation operation)
    {
        try { await operation.DrainAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            log.SafeError(nameof(OverlaySessionWorkflow), "drain-session-operation", exception);
        }
    }

    private async Task CloseOverlayAsync(IOverlaySession overlay)
    {
        try { await overlay.CloseAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            log.SafeError(nameof(OverlaySessionWorkflow), "close-overlay", exception);
        }
    }

    private async Task DisposeOverlayAsync(IOverlaySession overlay)
    {
        try { await overlay.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            log.SafeError(nameof(OverlaySessionWorkflow), "dispose-overlay", exception);
        }
    }
}
