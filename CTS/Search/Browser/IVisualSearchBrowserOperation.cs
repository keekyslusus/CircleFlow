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
}
