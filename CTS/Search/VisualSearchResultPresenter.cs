using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal sealed class VisualSearchResultPresenter(
    ISearchBrowserHost browserHost,
    Func<string, bool> openUrl,
    IPluginNotifier notifier,
    UiStrings strings,
    PluginLog log)
{
    public async Task PresentAsync(
        RoutedVisualSearchPreparation routed,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(routed);
        var prepared = routed.Outcome.PreparedSearch
                       ?? throw new ArgumentException(
                           "Only a successful preparation can be presented.",
                           nameof(routed));
        if (cancel.IsCancellationRequested) return;

        var shown = await browserHost
            .ShowAsync(
                new SearchProviderDescriptor(routed.ProviderId, routed.ProviderDisplayName),
                prepared,
                cancel)
            .ConfigureAwait(false);
        log.Info(
            nameof(VisualSearchResultPresenter),
            $"provider '{routed.ProviderId}' browser completed with {shown.Status}");
        if (shown.Status is SearchBrowserShowStatus.Shown or SearchBrowserShowStatus.Canceled ||
            cancel.IsCancellationRequested)
        {
            return;
        }

        var technicalFailure = shown.Status is SearchBrowserShowStatus.RuntimeUnavailable or
            SearchBrowserShowStatus.InitializationFailed or
            SearchBrowserShowStatus.NavigationFailed;
        if (technicalFailure && prepared.ExternalFallbackUrl is { } fallback)
        {
            if (cancel.IsCancellationRequested) return;
            var opened = false;
            try
            {
                opened = openUrl(fallback.AbsoluteUri);
            }
            catch (Exception exception)
            {
                log.Error(nameof(VisualSearchResultPresenter), "opening the external results URL failed", exception);
            }

            if (!opened)
                notifier.ShowError(strings.PluginTitle, strings.ResultsUrlOpenFailed);
            else
                log.Info(
                    nameof(VisualSearchResultPresenter),
                    $"provider '{routed.ProviderId}' results opened in the default browser");
            return;
        }

        var reason = shown.Status switch
        {
            SearchBrowserShowStatus.RuntimeUnavailable =>
                strings.BrowserRuntimeRequired(routed.ProviderDisplayName),
            SearchBrowserShowStatus.ProviderOperationFailed =>
                strings.BrowserImageAttachmentFailed(routed.ProviderDisplayName),
            _ => strings.SearchBrowserShowFailed(routed.ProviderDisplayName),
        };
        notifier.ShowError(strings.PluginTitle, reason);
    }
}
