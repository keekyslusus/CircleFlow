using System.Windows;
using System.Windows.Input;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.TextRecognition;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal interface IOverlayControllerFactory
{
    OverlayControllers Create(OverlayControllerContext context);
}

internal sealed class OverlayControllers(
    SelectionOverlayController selection,
    TextSelectionOverlayController textSelection,
    PointerGestureRouter pointer,
    OcrOverlayController ocr,
    ScreenTranslationOverlayController translation,
    ColorPickController colorPick,
    ProviderMenuController provider,
    MusicOverlayController music,
    ToastOverlayController toast,
    DebugOverlayController debug) : IDisposable
{
    private bool _disposed;

    internal SelectionOverlayController Selection { get; } = selection;
    internal TextSelectionOverlayController TextSelection { get; } = textSelection;
    internal PointerGestureRouter Pointer { get; } = pointer;
    internal OcrOverlayController Ocr { get; } = ocr;
    internal ScreenTranslationOverlayController Translation { get; } = translation;
    internal ColorPickController ColorPick { get; } = colorPick;
    internal ProviderMenuController Provider { get; } = provider;
    internal MusicOverlayController Music { get; } = music;
    internal ToastOverlayController Toast { get; } = toast;
    internal DebugOverlayController Debug { get; } = debug;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Music.Dispose();
        Debug.Dispose();
        Ocr.Dispose();
        Translation.Dispose();
        Pointer.Dispose();
        TextSelection.Dispose();
        Selection.Dispose();
        Provider.Dispose();
        ColorPick.Dispose();
        Toast.Dispose();
    }
}

internal sealed record OverlayControllerContext(
    OverlayVisual Visual,
    FrameworkElement CoordinateRoot,
    GdiBitmap FrozenFrame,
    GdiRectangle Monitor,
    double Scale,
    OverlayOptions Options,
    bool Overscan,
    IReadOnlyList<SearchProviderDescriptor> Providers,
    string SelectedProviderId,
    UiStrings Strings,
    bool DebugEnabled,
    Func<bool> CanAcceptSelectionInput,
    Func<object?, Point, bool> CanStartSelection,
    Action SelectionStarted,
    Action<GdiRectangle> SelectionCompleted,
    Action SelectionRejected,
    Action SelectionHoldCompleted,
    Action ColorConfirmationStarted,
    Action ColorPickFailed,
    Action ColorConfirmationCompleted,
    Func<bool> CanUseProvider,
    Action<string> ProviderSelected,
    Func<OverlayInteractionMode> GetMode,
    Action MusicStartRequested,
    Action MusicCancelRequested,
    Action<MusicDebugScenario> DebugScenarioSelected,
    Action<IOverlayCommand> MusicResultCommandRequested,
    Action<IOverlayCommand> PublishCommand,
    Action<OverlayInteractionMode> TransitionMode);

internal sealed class OverlayControllerFactory : IOverlayControllerFactory
{
    private readonly Action<string> _setClipboard;
    private readonly Func<bool> _animationsEnabled;
    private readonly Func<MouseEventArgs, Point>? _pointerPosition;
    private readonly IOcrRecognizer _ocrRecognizer;
    private readonly double _textHitToleranceDips;
    private readonly Func<string?> _ocrLanguageTag;
    private readonly Func<string> _targetLanguageTag;
    private readonly Func<bool> _translationConsentAccepted;
    private readonly Action _acceptTranslationConsent;
    private readonly PluginLog? _log;

    internal OverlayControllerFactory()
        : this(Clipboard.SetText, OverlayVisualResources.AnimationsEnabled)
    {
    }

    internal OverlayControllerFactory(
        Action<string> setClipboard,
        Func<bool> animationsEnabled,
        Func<MouseEventArgs, Point>? pointerPosition = null,
        IOcrRecognizer? ocrRecognizer = null,
        double textHitToleranceDips = 3,
        Func<string?>? ocrLanguageTag = null,
        Func<string>? targetLanguageTag = null,
        Func<bool>? translationConsentAccepted = null,
        Action? acceptTranslationConsent = null,
        PluginLog? log = null)
    {
        _setClipboard = setClipboard ?? throw new ArgumentNullException(nameof(setClipboard));
        _animationsEnabled = animationsEnabled ?? throw new ArgumentNullException(nameof(animationsEnabled));
        _pointerPosition = pointerPosition;
        _ocrRecognizer = ocrRecognizer ?? DisabledOcrRecognizer.Instance;
        if (!double.IsFinite(textHitToleranceDips) || textHitToleranceDips < 0)
            throw new ArgumentOutOfRangeException(nameof(textHitToleranceDips));
        _textHitToleranceDips = textHitToleranceDips;
        _ocrLanguageTag = ocrLanguageTag ?? (() => null);
        _targetLanguageTag = targetLanguageTag ?? (() => "en");
        _translationConsentAccepted = translationConsentAccepted ?? (() => true);
        _acceptTranslationConsent = acceptTranslationConsent ?? (() => { });
        _log = log;
    }

    public OverlayControllers Create(OverlayControllerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SelectionOverlayController? selection = null;
        TextSelectionOverlayController? textSelection = null;
        PointerGestureRouter? pointer = null;
        OcrOverlayController? ocr = null;
        ScreenTranslationOverlayController? translation = null;
        ColorPickController? colorPick = null;
        ProviderMenuController? provider = null;
        MusicOverlayController? music = null;
        ToastOverlayController? toast = null;
        DebugOverlayController? debug = null;
        try
        {
            toast = new ToastOverlayController(
                context.Visual.Bottom,
                context.Visual.LightTheme,
                _animationsEnabled);
            colorPick = new ColorPickController(
                context.FrozenFrame,
                _setClipboard,
                context.Strings,
                toast.Show,
                context.ColorConfirmationStarted,
                context.ColorPickFailed,
                context.ColorConfirmationCompleted);
            provider = new ProviderMenuController(
                context.Visual.Provider,
                context.Visual.Bottom.Root,
                context.Providers,
                context.SelectedProviderId,
                context.Strings,
                context.Visual.LightTheme,
                context.CanUseProvider,
                context.ProviderSelected);
            var mapper = new OverlayCoordinateMapper(context.Scale, context.Overscan, context.Monitor.Size);
            selection = new SelectionOverlayController(
                context.Visual.Selection,
                context.CoordinateRoot,
                context.Monitor,
                context.Scale,
                context.Options.PaddingPx,
                context.Options.MinDiagonalPx,
                context.Overscan,
                context.CanAcceptSelectionInput,
                context.CanStartSelection,
                context.SelectionStarted,
                colorPick.Pick,
                context.SelectionCompleted,
                context.SelectionRejected,
                context.SelectionHoldCompleted,
                _pointerPosition,
                subscribeInput: false);
            textSelection = new TextSelectionOverlayController(
                context.Visual.TextSelection,
                context.CoordinateRoot,
                context.Visual.Selection.InputSurface,
                mapper,
                new OcrTextHitTester(_textHitToleranceDips * context.Scale),
                _setClipboard,
                toast.Show,
                () => provider.SelectedProviderId,
                context.PublishCommand,
                context.Strings,
                context.Visual.LightTheme);
            pointer = new PointerGestureRouter(
                context.Visual.Selection,
                context.CoordinateRoot,
                selection,
                textSelection,
                context.CanAcceptSelectionInput,
                context.CanStartSelection,
                _pointerPosition);
            translation = new ScreenTranslationOverlayController(
                context.Visual.TranslationAction,
                context.Visual.TranslationOverlay,
                context.Visual.Effects,
                context.CoordinateRoot,
                mapper,
                context.Strings,
                _translationConsentAccepted,
                _acceptTranslationConsent,
                _targetLanguageTag,
                context.PublishCommand,
                context.TransitionMode,
                toast.Show,
                _animationsEnabled,
                context.Visual.LightTheme);
            var frameSource = (System.Windows.Media.Imaging.BitmapSource?)context.Visual.Selection.Screenshot.Source
                ?? throw new InvalidOperationException("The overlay frame source is missing.");
            ocr = new OcrOverlayController(frameSource, context.CoordinateRoot.Dispatcher, _ocrRecognizer, _ocrLanguageTag(), outcome =>
            {
                textSelection.SetDocument(outcome.Document);
                translation.SetOcrOutcome(outcome);
            }, _log);
            debug = new DebugOverlayController(
                context.Visual.Debug,
                context.Visual.LightTheme,
                context.DebugEnabled,
                context.GetMode,
                context.DebugScenarioSelected,
                toast.Show);
            music = new MusicOverlayController(
                context.Visual.Music,
                context.Visual.Bottom.LayoutTransitions,
                context.Visual.Effects,
                context.Visual.Root,
                context.Strings,
                context.Visual.LightTheme,
                context.GetMode,
                context.MusicStartRequested,
                context.MusicCancelRequested,
                context.MusicResultCommandRequested,
                _setClipboard,
                _animationsEnabled);
            return new OverlayControllers(
                selection,
                textSelection,
                pointer,
                ocr,
                translation,
                colorPick,
                provider,
                music,
                toast,
                debug);
        }
        catch
        {
            music?.Dispose();
            debug?.Dispose();
            ocr?.Dispose();
            translation?.Dispose();
            pointer?.Dispose();
            textSelection?.Dispose();
            selection?.Dispose();
            provider?.Dispose();
            colorPick?.Dispose();
            toast?.Dispose();
            throw;
        }
    }
}
