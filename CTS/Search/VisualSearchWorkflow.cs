using CircleToSearch.Capture;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Search;

internal sealed class VisualSearchWorkflow(
    VisualSearchProviderRouter providerRouter,
    Func<GdiBitmap, GdiRectangle, int, byte[]> crop,
    VisualSearchResultPresenter presenter,
    IPluginNotifier notifier,
    UiStrings strings,
    PluginLog log)
{
    internal async Task<VisualSearchPreparationOutcome> PrepareTraceAsync(
        SelectionOutcome selection, int maxLongSidePx, CancellationToken cancellationToken)
    {
        var jpeg = selection.Encode(crop, maxLongSidePx);
        var routed = await providerRouter.PrepareAsync(SearchProviderIds.TraceMoe, jpeg, cancellationToken).ConfigureAwait(false);
        return routed.Outcome;
    }

    public async Task ExecuteAsync(
        SelectionOutcome selection,
        string requestedProviderId,
        Action onUploadStarted,
        int maxLongSidePx,
        CancellationToken cancellationToken)
    {
        var jpeg = selection.Encode(crop, maxLongSidePx);

        onUploadStarted();
        var selectedProvider = providerRouter.GetEffectiveDescriptor(requestedProviderId);
        log.Info(nameof(VisualSearchWorkflow), $"upload started with provider '{selectedProvider.Id}'");
        var routed = await providerRouter.PrepareAsync(requestedProviderId, jpeg, cancellationToken).ConfigureAwait(false);
        var result = routed.Outcome;
        log.Info(nameof(VisualSearchWorkflow), $"provider '{routed.ProviderId}' preparation completed with {result.Failure}");
        if (!result.Success)
        {
            var reason = result.Failure switch
            {
                UploadFailure.UnexpectedStatus => strings.SearchUnexpectedStatus(result.StatusCode),
                UploadFailure.BadResponse => strings.SearchUnexpectedResponse,
                UploadFailure.PolicyRejection => strings.SearchUnexpectedResultsLocation,
                UploadFailure.Timeout => strings.SearchTimedOut,
                UploadFailure.NetworkError => strings.SearchNetworkError,
                UploadFailure.Canceled => null,
                _ => strings.SearchUploadFailed,
            };
            if (reason is null) return;
            log.Warn(nameof(VisualSearchWorkflow),
                $"provider '{routed.ProviderId}' failed: {result.Failure} status {result.StatusCode}");
            notifier.ShowError(strings.PluginTitle, reason);
            return;
        }

        await presenter.PresentAsync(routed, cancellationToken).ConfigureAwait(false);
    }
}
