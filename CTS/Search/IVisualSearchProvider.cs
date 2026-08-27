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
}

public sealed record VisualSearchOutcome(
    bool Success,
    string? ResultsUrl,
    LensUploadFailure Failure = LensUploadFailure.None,
    int? StatusCode = null)
{
    public static VisualSearchOutcome Ok(string url) => new(true, url);

    public static VisualSearchOutcome Fail(LensUploadFailure failure, int? statusCode = null)
        => new(false, null, failure, statusCode);
}
