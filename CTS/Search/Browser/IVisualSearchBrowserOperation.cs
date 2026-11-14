namespace CircleToSearch.Search.Browser;

public interface IVisualSearchBrowserOperation
{
    Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
        IVisualSearchBrowserSession session,
        CancellationToken cancel);
}

public enum VisualSearchBrowserOperationStatus
{
    Succeeded,
    Failed,
    Canceled,
    // The page itself could not be opened, as opposed to an image the page did not take.
    NavigationFailed,
}
