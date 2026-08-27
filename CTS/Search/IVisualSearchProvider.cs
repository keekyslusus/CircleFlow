namespace CircleToSearch.Search;

public interface IVisualSearchProvider
{
    Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel);
}

public enum LensUploadFailure
{
    None,
    UnexpectedStatus,
    EmptyLocation,
    PolicyRejection,
    Timeout,
    NetworkError,
    Canceled,
    ClipboardUnavailable,
    BrowserLaunchFailed,
}

public sealed record VisualSearchOutcome(
    bool Success,
    string? ResultsUrl,
    LensUploadFailure Failure = LensUploadFailure.None,
    int? StatusCode = null)
{
    public static VisualSearchOutcome Ok(string url) => new(true, url);

    // Success without a URL: the provider already delivered the results (paste flow).
    public static VisualSearchOutcome Handled() => new(true, null);

    public static VisualSearchOutcome Fail(LensUploadFailure failure, int? statusCode = null)
        => new(false, null, failure, statusCode);
}
