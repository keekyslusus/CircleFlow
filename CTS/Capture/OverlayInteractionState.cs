namespace CircleToSearch.Capture;

internal enum OverlayInteractionMode
{
    Selecting,
    Listening,
    MusicResult,
    Closing,
}

internal sealed class OverlayInteractionState
{
    public OverlayInteractionMode Mode { get; private set; } = OverlayInteractionMode.Selecting;

    public bool IsFinished => Mode == OverlayInteractionMode.Closing;

    public bool CanAcceptSelectionInput => Mode == OverlayInteractionMode.Selecting;

    public bool TransitionTo(OverlayInteractionMode target)
    {
        if (target == Mode) return false;
        if (!IsAllowed(Mode, target))
            throw new InvalidOperationException($"Overlay transition from {Mode} to {target} is not allowed.");
        Mode = target;
        return true;
    }

    private static bool IsAllowed(OverlayInteractionMode source, OverlayInteractionMode target) =>
        (source, target) switch
        {
            (OverlayInteractionMode.Selecting, OverlayInteractionMode.Listening) => true,
            (OverlayInteractionMode.Listening, OverlayInteractionMode.MusicResult) => true,
            (OverlayInteractionMode.MusicResult, OverlayInteractionMode.Selecting) => true,
            (OverlayInteractionMode.MusicResult, OverlayInteractionMode.Listening) => true,
            (_, OverlayInteractionMode.Closing) when source != OverlayInteractionMode.Closing => true,
            _ => false,
        };
}
