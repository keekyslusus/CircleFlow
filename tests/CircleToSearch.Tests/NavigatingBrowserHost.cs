using CircleToSearch.Search;
using CircleToSearch.Search.Browser;

namespace CircleToSearch.Tests;

// Runs browser operations against a fake page and, like SearchBrowserHost, keeps a warmed show hidden until its reveal.
internal sealed class NavigatingBrowserHost : ISearchBrowserHost
{
    public SearchBrowserShowStatus? ShowFailure { get; set; }
    public BrowserNavigationResult Navigation { get; set; } = BrowserNavigationResult.Succeeded();
    public List<string> Names { get; } = [];
    public List<Uri> Navigated { get; } = [];
    public List<string> Events { get; } = [];
    public int Shows => Names.Count;

    public async Task<SearchBrowserShowResult> ShowAsync(
        SearchProviderDescriptor descriptor,
        PreparedVisualSearch preparedSearch,
        CancellationToken cancel)
    {
        Names.Add(descriptor.DisplayName);
        Events.Add("show");
        if (ShowFailure is { } failure) return new SearchBrowserShowResult(failure);

        var session = new FakeBrowserSession();
        session.Navigations.Enqueue(Navigation);
        var execution = preparedSearch.RequireBrowserOperation().ExecuteAsync(session, cancel);
        VisualSearchBrowserOperationStatus status;
        try
        {
            status = await execution;
            if (preparedSearch.RevealAfter is { } reveal) await reveal.WaitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            status = VisualSearchBrowserOperationStatus.Canceled;
        }
        Navigated.AddRange(session.GetTargets);
        if (status == VisualSearchBrowserOperationStatus.Succeeded) Events.Add("revealed");
        return new SearchBrowserShowResult(status switch
        {
            VisualSearchBrowserOperationStatus.Succeeded => SearchBrowserShowStatus.Shown,
            VisualSearchBrowserOperationStatus.Canceled => SearchBrowserShowStatus.Canceled,
            _ => SearchBrowserShowStatus.ProviderOperationFailed,
        });
    }
}
