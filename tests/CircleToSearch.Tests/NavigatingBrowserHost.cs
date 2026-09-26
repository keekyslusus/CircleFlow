using System.IO;
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
    public List<string> Scripts { get; } = [];
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

        var execution = preparedSearch.RequireBrowserOperation().ExecuteAsync(new Page(this), cancel);
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
        if (status == VisualSearchBrowserOperationStatus.Succeeded) Events.Add("revealed");
        return new SearchBrowserShowResult(status switch
        {
            VisualSearchBrowserOperationStatus.Succeeded => SearchBrowserShowStatus.Shown,
            VisualSearchBrowserOperationStatus.Canceled => SearchBrowserShowStatus.Canceled,
            _ => SearchBrowserShowStatus.ProviderOperationFailed,
        });
    }

    private sealed class Page(NavigatingBrowserHost host) : IVisualSearchBrowserSession
    {
        public Uri? CurrentUri { get; private set; }

        public Task<BrowserNavigationResult> NavigateAsync(Uri target, TimeSpan timeout, CancellationToken cancel)
        {
            host.Navigated.Add(target);
            CurrentUri = target;
            return Task.FromResult(host.Navigation);
        }

        public Task<string> ExecuteScriptAsync(string script, CancellationToken cancel)
        {
            host.Scripts.Add(script);
            return Task.FromResult("null");
        }

        public Task<BrowserNavigationResult> NavigatePostAsync(Uri target, Stream body, string headers,
            TimeSpan timeout, CancellationToken cancel) => throw new NotSupportedException();

        public Task<BrowserNavigationResult> WaitForNavigationAsync(TimeSpan timeout, CancellationToken cancel) =>
            throw new NotSupportedException();

        public Task<string?> PostWebMessageAndWaitAsync(string message, Func<string, bool> predicate,
            TimeSpan timeout, CancellationToken cancel) => throw new NotSupportedException();
    }
}
