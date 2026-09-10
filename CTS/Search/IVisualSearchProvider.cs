namespace CircleToSearch.Search;

public interface IVisualSearchProvider
{
    Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] jpeg, CancellationToken cancel);
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
}

public sealed class VisualSearchPreparationOutcome
{
    private VisualSearchPreparationOutcome(
        PreparedVisualSearch? preparedSearch,
        UploadFailure failure,
        int? statusCode)
    {
        PreparedSearch = preparedSearch;
        Failure = failure;
        StatusCode = statusCode;
    }

    public bool Success => PreparedSearch is not null;

    public PreparedVisualSearch? PreparedSearch { get; }

    public UploadFailure Failure { get; }

    public int? StatusCode { get; }

    public static VisualSearchPreparationOutcome Ready(PreparedVisualSearch preparedSearch)
    {
        ArgumentNullException.ThrowIfNull(preparedSearch);
        return new VisualSearchPreparationOutcome(preparedSearch, UploadFailure.None, null);
    }

    public static VisualSearchPreparationOutcome Fail(UploadFailure failure, int? statusCode = null)
    {
        if (failure == UploadFailure.None)
            throw new ArgumentOutOfRangeException(nameof(failure));
        return new VisualSearchPreparationOutcome(null, failure, statusCode);
    }
}
