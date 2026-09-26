using CircleToSearch.Search.Browser;
using CircleToSearch.Shell;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal sealed class TextSearchWorkflow(
    TextSearchUrlBuilder urlBuilder,
    Func<string> engineId,
    Func<bool> openInBuiltInBrowser,
    ISearchBrowserHost browserHost,
    UrlOpeningService urlOpening,
    IPluginNotifier notifier,
    UiStrings strings,
    PluginLog log)
{
    private const string BuiltInBrowserProviderId = "text-search";

    public bool OpensInBuiltInBrowser => openInBuiltInBrowser();

    public async Task ExecuteAsync(string text, string providerId, Action onBrowserStarted,
        CancellationToken cancellationToken)
    {
        var engine = engineId();
        if (TryBuild(text, providerId, engine) is not { } results) return;
        if (!OpensInBuiltInBrowser)
        {
            if (urlOpening.TryOpen(results.AbsoluteUri, strings.TextSearchOpenFailed))
                log.Info(nameof(TextSearchWorkflow), $"text search opened with provider '{providerId}', engine '{engine}'");
            return;
        }

        log.Info(nameof(TextSearchWorkflow),
            $"text search opening in the built-in browser with provider '{providerId}', engine '{engine}'");
        onBrowserStarted();
        await PresentAsync(urlBuilder.SiteName(providerId, engine), Task.FromResult(results), revealAfter: null,
            cancellationToken).ConfigureAwait(false);
    }

    // Null when the selected provider has no text search, so there is nothing to warm.
    public Task? WarmAsync(string providerId, Task<Uri> results, Task revealAfter, CancellationToken cancellationToken)
    {
        var engine = engineId();
        string site;
        try { site = urlBuilder.SiteName(providerId, engine); }
        catch (ArgumentException) { return null; }
        log.Info(nameof(TextSearchWorkflow), "warming the built-in browser while text is selected");
        return PresentAsync(site, results, revealAfter, cancellationToken);
    }

    // Quiet because the caller closes the overlay first and then reports the failure through ExecuteAsync.
    public Uri? TryCreateResultsUrl(string text, string providerId)
    {
        try { return new Uri(urlBuilder.Build(text, providerId, engineId())); }
        catch (ArgumentException) { return null; }
    }

    private Uri? TryBuild(string text, string providerId, string engine)
    {
        try { return new Uri(urlBuilder.Build(text, providerId, engine)); }
        catch (TextSearchQueryTooLongException)
        {
            notifier.ShowError(strings.PluginTitle, strings.TextSearchTooLong);
        }
        catch (ArgumentException exception)
        {
            log.Warn(nameof(TextSearchWorkflow), $"text search policy rejected the request: {exception.GetType().Name}");
            notifier.ShowError(strings.PluginTitle, strings.TextSearchOpenFailed);
        }
        return null;
    }

    private async Task PresentAsync(string site, Task<Uri> results, Task? revealAfter, CancellationToken cancel)
    {
        var shown = await browserHost.ShowAsync(
            new SearchProviderDescriptor(BuiltInBrowserProviderId, site),
            PreparedVisualSearch.ForBrowserOperation(
                new TextSearchBrowserOperation(results), externalFallbackUrl: null, revealAfter),
            cancel).ConfigureAwait(false);
        if (shown.Status is SearchBrowserShowStatus.Shown or SearchBrowserShowStatus.Canceled ||
            cancel.IsCancellationRequested)
            return;

        if (revealAfter is not null)
        {
            // A browser warmed in the background has failed only once the user has actually searched.
            try { await revealAfter.WaitAsync(cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
        if (!results.IsCompletedSuccessfully) return;
        log.Warn(nameof(TextSearchWorkflow), $"built-in browser completed with {shown.Status}; using the default browser");
        urlOpening.TryOpen(results.Result.AbsoluteUri, strings.TextSearchOpenFailed);
    }
}
