namespace CircleToSearch.Search;

// Runs the primary provider and falls back to the secondary one when the primary fails for any
// reason other than a user cancellation.
public sealed class FallbackVisualSearchProvider : IVisualSearchProvider
{
    private readonly IVisualSearchProvider _primary;
    private readonly IVisualSearchProvider _fallback;

    public FallbackVisualSearchProvider(IVisualSearchProvider primary, IVisualSearchProvider fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public async Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
    {
        var outcome = await _primary.SearchAsync(png, cancel).ConfigureAwait(false);
        if (outcome.Success || outcome.Failure == LensUploadFailure.Canceled) return outcome;

        var fallback = await _fallback.SearchAsync(png, cancel).ConfigureAwait(false);
        return fallback.Success ? fallback : outcome;
    }
}
