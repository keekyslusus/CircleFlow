namespace CircleToSearch.Capture;

internal enum OverlayInteractionMode
{
    Selecting,
    TraceLoading,
    TraceResult,
    Listening,
    MusicResult,
    TranslationConsent,
    Translating,
    TranslationShown,
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
            (OverlayInteractionMode.Selecting, OverlayInteractionMode.TraceLoading) => true,
            (OverlayInteractionMode.TraceLoading, OverlayInteractionMode.TraceResult) => true,
            (OverlayInteractionMode.TraceResult, OverlayInteractionMode.Selecting) => true,
            (OverlayInteractionMode.Selecting, OverlayInteractionMode.Listening) => true,
            (OverlayInteractionMode.Listening, OverlayInteractionMode.MusicResult) => true,
            (OverlayInteractionMode.MusicResult, OverlayInteractionMode.Selecting) => true,
            (OverlayInteractionMode.MusicResult, OverlayInteractionMode.Listening) => true,
            (OverlayInteractionMode.Selecting, OverlayInteractionMode.TranslationConsent) => true,
            (OverlayInteractionMode.TranslationConsent, OverlayInteractionMode.Selecting) => true,
            (OverlayInteractionMode.TranslationConsent, OverlayInteractionMode.Translating) => true,
            (OverlayInteractionMode.Selecting, OverlayInteractionMode.Translating) => true,
            (OverlayInteractionMode.Translating, OverlayInteractionMode.TranslationShown) => true,
            (OverlayInteractionMode.Translating, OverlayInteractionMode.Selecting) => true,
            (OverlayInteractionMode.TranslationShown, OverlayInteractionMode.Selecting) => true,
            (_, OverlayInteractionMode.Closing) when source != OverlayInteractionMode.Closing => true,
            _ => false,
        };
}
