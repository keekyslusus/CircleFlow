namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class OverlayControllers(
    SelectionOverlayController selection,
    TextSelectionOverlayController textSelection,
    PointerGestureRouter pointer,
    OcrOverlayController ocr,
    ScreenTranslationOverlayController translation,
    ProviderMenuController provider,
    MusicOverlayController music,
    TraceOverlayController trace,
    ActionTrayOverlayController actionTray,
    ToastOverlayController toast,
    DebugOverlayController debug) : IDisposable
{
    private bool _disposed;

    internal SelectionOverlayController Selection { get; } = selection;
    internal TextSelectionOverlayController TextSelection { get; } = textSelection;
    internal PointerGestureRouter Pointer { get; } = pointer;
    internal OcrOverlayController Ocr { get; } = ocr;
    internal ScreenTranslationOverlayController Translation { get; } = translation;
    internal ProviderMenuController Provider { get; } = provider;
    internal MusicOverlayController Music { get; } = music;
    internal TraceOverlayController Trace { get; } = trace;
    internal ActionTrayOverlayController ActionTray { get; } = actionTray;
    internal ToastOverlayController Toast { get; } = toast;
    internal DebugOverlayController Debug { get; } = debug;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Trace.Dispose();
        ActionTray.Dispose();
        Music.Dispose();
        Debug.Dispose();
        Ocr.Dispose();
        Translation.Dispose();
        Pointer.Dispose();
        TextSelection.Dispose();
        Selection.Dispose();
        Provider.Dispose();
        Toast.Dispose();
    }
}
