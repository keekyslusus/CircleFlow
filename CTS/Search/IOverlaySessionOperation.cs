namespace CircleToSearch.Search;

internal enum OverlaySessionContinuation
{
    Continue,
    EndSession,
}

internal interface IOverlaySessionOperation
{
    Task? PendingTask { get; }
    Task<OverlaySessionContinuation> CompletePendingAsync();
    void RequestStop();
    Task DrainAsync();
}
