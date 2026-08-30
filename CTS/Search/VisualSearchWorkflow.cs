using CircleToSearch.Capture;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Search;

internal sealed class VisualSearchWorkflow(
    VisualSearchProviderRouter providerRouter,
    Func<GdiBitmap, GdiRectangle, byte[]> crop,
    Func<string, bool> openUrl,
    IPluginNotifier notifier,
    UiStrings strings,
    PluginLog log)
{
    public async Task ExecuteAsync(
        SelectionOutcome selection,
        string requestedProviderId,
        Action onUploadStarted,
        CancellationToken cancellationToken)
    {
        byte[] png;
        try { png = crop(selection.FrozenFrame, selection.Bounds); }
        finally { selection.FrozenFrame.Dispose(); }

        onUploadStarted();
        var selectedProvider = providerRouter.GetEffectiveDescriptor(requestedProviderId);
        log.Info(nameof(VisualSearchWorkflow), $"upload started with provider '{selectedProvider.Id}'");
        var routed = await providerRouter.SearchAsync(requestedProviderId, png, cancellationToken).ConfigureAwait(false);
        var result = routed.Outcome;
        log.Info(nameof(VisualSearchWorkflow), $"provider '{routed.ProviderId}' completed with {result.Failure}");
        if (!result.Success)
        {
            var reason = result.Failure switch
            {
                UploadFailure.UnexpectedStatus => strings.SearchUnexpectedStatus(result.StatusCode),
                UploadFailure.BadResponse => strings.SearchUnexpectedResponse,
                UploadFailure.PolicyRejection => strings.SearchUnexpectedResultsLocation,
                UploadFailure.Timeout => strings.SearchTimedOut,
                UploadFailure.NetworkError => strings.SearchNetworkError,
                UploadFailure.BrowserRuntimeUnavailable => strings.BrowserRuntimeRequired(routed.ProviderDisplayName),
                UploadFailure.BrowserAutomationFailed => strings.BrowserImageAttachmentFailed(routed.ProviderDisplayName),
                UploadFailure.Canceled => null,
                _ => strings.SearchUploadFailed,
            };
            if (reason is null) return;
            log.Warn(nameof(VisualSearchWorkflow),
                $"provider '{routed.ProviderId}' failed: {result.Failure} status {result.StatusCode}");
            notifier.ShowError(strings.PluginTitle, reason);
            return;
        }

        if (result.ResultsUrl is { Length: > 0 } url)
        {
            if (!openUrl(url)) notifier.ShowError(strings.PluginTitle, strings.ResultsUrlOpenFailed);
            else log.Info(nameof(VisualSearchWorkflow),
                $"provider '{routed.ProviderId}' results opened in the default browser");
        }
        else log.Info(nameof(VisualSearchWorkflow), $"results delivered by provider '{routed.ProviderId}'");
    }
}
