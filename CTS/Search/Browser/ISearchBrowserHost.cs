namespace CircleToSearch.Search.Browser;

public interface ISearchBrowserHost
{
    Task<SearchBrowserShowResult> ShowAsync(
        SearchProviderDescriptor descriptor,
        PreparedVisualSearch preparedSearch,
        CancellationToken cancel);
}

public enum SearchBrowserShowStatus
{
    Shown,
    Canceled,
    RuntimeUnavailable,
    InitializationFailed,
    NavigationFailed,
    ProviderOperationFailed,
}

public sealed record SearchBrowserShowResult(SearchBrowserShowStatus Status);
