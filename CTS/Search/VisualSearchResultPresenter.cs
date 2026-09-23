using CircleToSearch.Search.Browser;
using CircleToSearch.Shell;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal sealed class VisualSearchResultPresenter(
    ISearchBrowserHost browserHost,
    UrlOpeningService urlOpening,
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

        if (prepared.RevealAfter is { } reveal)
        {
            // A search prepared in the background reports failure only once the user has submitted it.
            try { await reveal.WaitAsync(cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }

        var technicalFailure = shown.Status is SearchBrowserShowStatus.RuntimeUnavailable or
            SearchBrowserShowStatus.InitializationFailed or
            SearchBrowserShowStatus.NavigationFailed;
        if (technicalFailure && prepared.ExternalFallbackUrl is { } fallback)
        {
            if (cancel.IsCancellationRequested) return;
            if (urlOpening.TryOpen(fallback.AbsoluteUri))
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
