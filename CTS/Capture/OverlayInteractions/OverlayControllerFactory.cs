using System.Windows;
using System.Windows.Input;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal interface IOverlayControllerFactory
{
    OverlayControllers Create(OverlayControllerContext context);
}

internal delegate TraceOverlayController TraceOverlayControllerFactory(
    OverlayControllerContext context,
    ClipboardCopyService clipboardCopy);

internal delegate ActionTrayOverlayController ActionTrayOverlayControllerFactory(
    OverlayControllerContext context);

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

internal sealed record OverlayControllerContext(
    OverlayVisual Visual,
    FrameworkElement CoordinateRoot,
    GdiRectangle Monitor,
    double Scale,
    OverlayOptions Options,
    bool Overscan,
    IReadOnlyList<SearchProviderDescriptor> Providers,
    string SelectedProviderId,
    UiStrings Strings,
    Action<IOverlayCommand>? PublishCommand,
    Func<GdiRectangle, SelectionOutcome> CreateSelectionCopy,
    Func<bool> CanAcceptSelectionInput,
    Func<object?, Point, bool> CanStartSelection,
    Action SelectionStarted,
    Action<GdiRectangle> SelectionCompleted,
    Action SelectionRejected,
    Action SelectionHoldCompleted,
    Func<bool> CanUseProvider,
    Action<string> ProviderSelected,
    Func<OverlayInteractionMode> GetMode,
    Action MusicStartRequested,
    Action MusicCancelRequested,
    Action<MusicDebugScenario> DebugScenarioSelected,
    Action<IOverlayCommand> MusicResultCommandRequested,
    Action<OverlayInteractionMode> TransitionMode,
    string? OcrLanguageTag = null,
    string TranslationTargetLanguageTag = "en",
    Action? RequestCancel = null);

internal sealed class OverlayControllerFactory : IOverlayControllerFactory
{
    private readonly Action<string> _setClipboard;
    private readonly Func<bool> _animationsEnabled;
    private readonly Func<MouseEventArgs, Point>? _pointerPosition;
    private readonly IOcrRecognizer _ocrRecognizer;
    private readonly double _textHitToleranceDips;
    private readonly Func<bool> _translationConsentAccepted;
    private readonly Action _acceptTranslationConsent;
    private readonly Action? _resetTranslationConsent;
    private readonly PluginLog? _log;
    private readonly TranslationMemoryProfiler? _memoryProfiler;
    private readonly TraceOverlayControllerFactory _createTraceController;
    private readonly ActionTrayOverlayControllerFactory _createActionTrayController;

    internal OverlayControllerFactory(
        TraceOverlayControllerFactory createTraceController,
        ActionTrayOverlayControllerFactory createActionTrayController,
        Action<string> setClipboard,
        Func<bool> animationsEnabled,
        Func<MouseEventArgs, Point>? pointerPosition = null,
        IOcrRecognizer? ocrRecognizer = null,
        double textHitToleranceDips = 3,
        Func<bool>? translationConsentAccepted = null,
        Action? acceptTranslationConsent = null,
        Action? resetTranslationConsent = null,
        PluginLog? log = null,
        TranslationMemoryProfiler? memoryProfiler = null)
    {
        _createTraceController = createTraceController ?? throw new ArgumentNullException(nameof(createTraceController));
        _createActionTrayController = createActionTrayController ?? throw new ArgumentNullException(nameof(createActionTrayController));
        _setClipboard = setClipboard ?? throw new ArgumentNullException(nameof(setClipboard));
        _animationsEnabled = animationsEnabled ?? throw new ArgumentNullException(nameof(animationsEnabled));
        _pointerPosition = pointerPosition;
        _ocrRecognizer = ocrRecognizer ?? DisabledOcrRecognizer.Instance;
        if (!double.IsFinite(textHitToleranceDips) || textHitToleranceDips < 0)
            throw new ArgumentOutOfRangeException(nameof(textHitToleranceDips));
        _textHitToleranceDips = textHitToleranceDips;
        _translationConsentAccepted = translationConsentAccepted ?? (() => true);
        _acceptTranslationConsent = acceptTranslationConsent ?? (() => { });
        _resetTranslationConsent = resetTranslationConsent;
        _log = log;
        _memoryProfiler = memoryProfiler;
    }

    public OverlayControllers Create(OverlayControllerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SelectionOverlayController? selection = null;
        TextSelectionOverlayController? textSelection = null;
        PointerGestureRouter? pointer = null;
        OcrOverlayController? ocr = null;
        ScreenTranslationOverlayController? translation = null;
        ProviderMenuController? provider = null;
        MusicOverlayController? music = null;
        TraceOverlayController? trace = null;
        ActionTrayOverlayController? actionTray = null;
        ToastOverlayController? toast = null;
        DebugOverlayController? debug = null;
        try
        {
            toast = new ToastOverlayController(
                context.Visual.Bottom,
                context.Visual.LightTheme,
                _animationsEnabled);
            var clipboardCopy = new ClipboardCopyService(_setClipboard, toast.Show, context.Strings);
            var publishCommand = context.PublishCommand ?? (_ => { });
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
                context.SelectionCompleted,
                context.SelectionRejected,
                context.SelectionHoldCompleted,
                context.Visual.ImageActions,
                context.CreateSelectionCopy,
                clipboardCopy,
                context.RequestCancel,
                _pointerPosition,
                subscribeInput: false);
            textSelection = new TextSelectionOverlayController(
                context.Visual.TextSelection,
                context.CoordinateRoot,
                context.Visual.Selection.InputSurface,
                mapper,
                new OcrTextHitTester(_textHitToleranceDips * context.Scale),
                clipboardCopy,
                () => provider.SelectedProviderId,
                publishCommand,
                context.Strings,
                context.Visual.LightTheme);
            pointer = new PointerGestureRouter(
                context.Visual.Selection,
                context.CoordinateRoot,
                selection,
                textSelection,
                () => context.CanAcceptSelectionInput() || (translation?.IsImageShown == true && context.GetMode() == OverlayInteractionMode.TranslationShown),
                context.CanStartSelection,
                _pointerPosition,
                onRightClickCancel: context.RequestCancel);
            translation = new ScreenTranslationOverlayController(
                context.Visual.TranslationAction,
                context.Visual.TranslationOverlay,
                context.Visual.Bottom,
                context.Visual.Effects,
                context.CoordinateRoot,
                context.Strings,
                _translationConsentAccepted,
                _acceptTranslationConsent,
                () => context.TranslationTargetLanguageTag,
                publishCommand,
                context.TransitionMode,
                toast.Show,
                _animationsEnabled,
                context.Visual.LightTheme,
                context.Visual.Selection.Screenshot,
                (image, language) =>
                {
                    pointer.Cancel();
                    textSelection.SetDocument(null);
                    if (context.Visual.Actions.Prompt is { } prompt)
                        prompt.Text = language is null ? context.Strings.SelectionPrompt : context.Strings.TranslatedTextPrompt;
                    ocr?.Restart(image, language ?? context.OcrLanguageTag);
                }, _memoryProfiler);
            var frameSource = (System.Windows.Media.Imaging.BitmapSource?)context.Visual.Selection.Screenshot.Source
                ?? throw new InvalidOperationException("The overlay frame source is missing.");
            ocr = new OcrOverlayController(frameSource, context.CoordinateRoot.Dispatcher, _ocrRecognizer, context.OcrLanguageTag, outcome =>
            {
                textSelection.SetDocument(outcome.Document);
            }, _log, _memoryProfiler);
            debug = new DebugOverlayController(
                context.Visual.Debug,
                context.Visual.LightTheme,
                context.PublishCommand is not null,
                context.GetMode,
                context.DebugScenarioSelected,
                toast.Show,
                _resetTranslationConsent,
                context.Strings);
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
                clipboardCopy,
                _animationsEnabled);
            trace = _createTraceController(context, clipboardCopy);
            actionTray = _createActionTrayController(context);
            return new OverlayControllers(
                selection,
                textSelection,
                pointer,
                ocr,
                translation,
                provider,
                music,
                trace,
                actionTray,
                toast,
                debug);
        }
        catch
        {
            actionTray?.Dispose();
            trace?.Dispose();
            music?.Dispose();
            debug?.Dispose();
            ocr?.Dispose();
            translation?.Dispose();
            pointer?.Dispose();
            textSelection?.Dispose();
            selection?.Dispose();
            provider?.Dispose();
            toast?.Dispose();
            throw;
        }
    }
}
