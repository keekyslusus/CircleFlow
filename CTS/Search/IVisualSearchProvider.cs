namespace CircleToSearch.Search;

public interface IVisualSearchProvider
{
    Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel);
}

public enum UploadFailure
{
    None,
    UnexpectedStatus,
    BadResponse,
    PolicyRejection,
    Timeout,
    NetworkError,
    Canceled,
    BrowserRuntimeUnavailable,
    BrowserAutomationFailed,
}

public sealed record VisualSearchOutcome(
    bool Success,
    string? ResultsUrl,
    UploadFailure Failure = UploadFailure.None,
    int? StatusCode = null)
{
    public static VisualSearchOutcome Ok(string url) => new(true, url);

    public static VisualSearchOutcome Handled() => new(true, null);

    public static VisualSearchOutcome Fail(UploadFailure failure, int? statusCode = null)
        => new(false, null, failure, statusCode);
}
