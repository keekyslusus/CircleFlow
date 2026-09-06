using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

public sealed class GoogleLensProvider : IVisualSearchProvider
{
    private readonly Func<byte[], IVisualSearchBrowserOperation> _createOperation;

    internal GoogleLensProvider(Func<byte[], IVisualSearchBrowserOperation> createOperation)
    {
        _createOperation = createOperation ?? throw new ArgumentNullException(nameof(createOperation));
    }

    public Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] png, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (cancel.IsCancellationRequested)
            return Task.FromResult(VisualSearchPreparationOutcome.Fail(UploadFailure.Canceled));

        var operation = _createOperation(png);
        return Task.FromResult(VisualSearchPreparationOutcome.Ready(
            PreparedVisualSearch.ForBrowserOperation(operation, externalFallbackUrl: null)));
    }
}
