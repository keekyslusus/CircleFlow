using System.IO;

namespace CircleToSearch.Search.Browser;

public interface IVisualSearchBrowserSession
{
    Uri? CurrentUri { get; }

    // For an operation that ends up showing another provider's results than the one the window opened with.
    void SetDisplayedProvider(SearchProviderDescriptor provider);

    Task<BrowserNavigationResult> NavigateAsync(
        Uri target,
        TimeSpan timeout,
        CancellationToken cancel);

    Task<BrowserNavigationResult> NavigatePostAsync(
        Uri target,
        Stream body,
        string headers,
        TimeSpan timeout,
        CancellationToken cancel);

    Task<BrowserNavigationResult> WaitForNavigationAsync(
        TimeSpan timeout,
        CancellationToken cancel);

    Task<string> ExecuteScriptAsync(string script, CancellationToken cancel);

    Task<string?> PostWebMessageAndWaitAsync(
        string message,
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancel);
}

public enum BrowserNavigationStatus
{
    Succeeded,
    Failed,
    TimedOut,
    Canceled,
}

public sealed record BrowserNavigationResult(
    BrowserNavigationStatus Status,
    string? Error = null)
{
    public static BrowserNavigationResult Succeeded() => new(BrowserNavigationStatus.Succeeded);

    public static BrowserNavigationResult Failed(string? error = null) =>
        new(BrowserNavigationStatus.Failed, error);

    public static BrowserNavigationResult TimedOut() => new(BrowserNavigationStatus.TimedOut);

    public static BrowserNavigationResult Canceled() => new(BrowserNavigationStatus.Canceled);
}
