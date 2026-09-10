using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

public sealed class GoogleLensProvider : IVisualSearchProvider
{
    private readonly Func<byte[], IVisualSearchBrowserOperation> _createOperation;

    internal GoogleLensProvider(Func<byte[], IVisualSearchBrowserOperation> createOperation)
    {
        _createOperation = createOperation ?? throw new ArgumentNullException(nameof(createOperation));
    }

    public Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] jpeg, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (cancel.IsCancellationRequested)
            return Task.FromResult(VisualSearchPreparationOutcome.Fail(UploadFailure.Canceled));

        var operation = _createOperation(jpeg);
        return Task.FromResult(VisualSearchPreparationOutcome.Ready(
            PreparedVisualSearch.ForBrowserOperation(operation, externalFallbackUrl: null)));
    }
}
