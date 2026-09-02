namespace CircleToSearch.Capture;

internal enum OverlayInteractionMode
{
    Selecting,
    Listening,
    MusicResult,
    ColorConfirmation,
    TextSelection,
    Translation,
    Closing,
}

internal sealed class OverlayInteractionState
{
    public OverlayInteractionMode Mode { get; private set; } = OverlayInteractionMode.Selecting;

    public bool IsFinished => Mode == OverlayInteractionMode.Closing;

    public bool CanAcceptSelectionInput =>
        Mode == OverlayInteractionMode.Selecting || Mode == OverlayInteractionMode.TextSelection;

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
            (OverlayInteractionMode.Selecting, OverlayInteractionMode.ColorConfirmation) => true,
            (OverlayInteractionMode.Selecting, OverlayInteractionMode.TextSelection) => true,
            (OverlayInteractionMode.TextSelection, OverlayInteractionMode.Selecting) => true,
            (OverlayInteractionMode.Selecting, OverlayInteractionMode.Translation) => true,
            (OverlayInteractionMode.Translation, OverlayInteractionMode.Selecting) => true,
            (OverlayInteractionMode.TextSelection, OverlayInteractionMode.Translation) => true,
            (OverlayInteractionMode.Translation, OverlayInteractionMode.TextSelection) => true,
            (_, OverlayInteractionMode.Closing) when source != OverlayInteractionMode.Closing => true,
            _ => false,
        };
}
