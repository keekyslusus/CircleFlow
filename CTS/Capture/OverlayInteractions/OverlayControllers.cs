namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class OverlayControllers(
    SelectionOverlayController selection,
    TextSelectionOverlayController textSelection,
    PointerGestureRouter pointer,
    OverlayImageTextCoordinator imageText,
    CircleToSearch.Interop.KeyboardInputLanguageSource inputLanguage,
    OcrOverlayController ocr,
    ScreenTranslationOverlayController translation,
    ProviderMenuController provider,
    MusicOverlayController music,
    WidgetOverlayController widget,
    ActionTrayOverlayController actionTray,
    ToastOverlayController toast,
    DebugOverlayController debug,
    OverlayActivityPresenter activityPresenter,
    ImageSelectionOverlayController imageSelection,
    SelectionHintOverlayController selectionHint,
    QrCodeOverlayController qrCodes,
    IDisposable? sounds = null) : IDisposable
{
    private bool _disposed;

    internal SelectionOverlayController Selection { get; } = selection;
    internal ImageSelectionOverlayController ImageSelection { get; } = imageSelection;
    internal TextSelectionOverlayController TextSelection { get; } = textSelection;
    internal PointerGestureRouter Pointer { get; } = pointer;
    internal OverlayImageTextCoordinator ImageText { get; } = imageText;
    internal CircleToSearch.Interop.KeyboardInputLanguageSource InputLanguage { get; } = inputLanguage;
    internal OcrOverlayController Ocr { get; } = ocr;
    internal ScreenTranslationOverlayController Translation { get; } = translation;
    internal ProviderMenuController Provider { get; } = provider;
    internal MusicOverlayController Music { get; } = music;
    internal WidgetOverlayController Widget { get; } = widget;
    internal ActionTrayOverlayController ActionTray { get; } = actionTray;
    internal ToastOverlayController Toast { get; } = toast;
    internal DebugOverlayController Debug { get; } = debug;
    internal SelectionHintOverlayController SelectionHint { get; } = selectionHint;
    internal QrCodeOverlayController QrCodes { get; } = qrCodes;
    private OverlayActivityPresenter ActivityPresenter { get; } = activityPresenter;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ImageSelection.Dispose();
        QrCodes.Dispose();
        Widget.Dispose();
        ActionTray.Dispose();
        SelectionHint.Dispose();
        Music.Dispose();
        Debug.Dispose();
        ImageText.Dispose();
        InputLanguage.Dispose();
        Ocr.Dispose();
        Translation.Dispose();
        Pointer.Dispose();
        TextSelection.Dispose();
        Selection.Dispose();
        Provider.Dispose();
        Toast.Dispose();
        ActivityPresenter.Dispose();
        sounds?.Dispose();
    }
}
