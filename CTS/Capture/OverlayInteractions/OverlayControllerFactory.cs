using System.Windows;
using System.Windows.Input;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Translation;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal interface IOverlayControllerFactory
{
    OverlayControllers Create(OverlayControllerContext context);
}

internal sealed class OverlayControllers(
    SelectionOverlayController selection,
    ColorPickController colorPick,
    ProviderMenuController provider,
    MusicOverlayController music,
    ToastOverlayController toast,
    DebugOverlayController debug,
    TextOverlayController? text = null,
    TranslationOverlayController? translation = null) : IDisposable
{
    private bool _disposed;

    internal SelectionOverlayController Selection { get; } = selection;
    internal ColorPickController ColorPick { get; } = colorPick;
    internal ProviderMenuController Provider { get; } = provider;
    internal MusicOverlayController Music { get; } = music;
    internal ToastOverlayController Toast { get; } = toast;
    internal DebugOverlayController Debug { get; } = debug;
    internal TextOverlayController? Text { get; } = text;
    internal TranslationOverlayController? Translation { get; } = translation;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Translation?.Dispose();
        Text?.Dispose();
        Debug.Dispose();
        Music.Dispose();
        Provider.Dispose();
        Selection.Dispose();
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
    Window? Window = null,
    Action<OverlayInteractionMode>? SetMode = null,
    ITranslationService? TranslationService = null);

internal sealed class OverlayControllerFactory : IOverlayControllerFactory
{
    private readonly Action<string> _setClipboard;
    private readonly Func<bool> _animationsEnabled;
    private readonly Func<MouseEventArgs, Point>? _pointerPosition;
    private readonly ITranslationService? _translationService;

    internal OverlayControllerFactory(
        ITranslationService? translationService = null)
        : this(Clipboard.SetText, OverlayVisualResources.AnimationsEnabled, translationService: translationService)
    {
    }

    internal OverlayControllerFactory(
        Action<string> setClipboard,
        Func<bool> animationsEnabled,
        Func<MouseEventArgs, Point>? pointerPosition = null,
        ITranslationService? translationService = null)
    {
        _setClipboard = setClipboard ?? throw new ArgumentNullException(nameof(setClipboard));
        _animationsEnabled = animationsEnabled ?? throw new ArgumentNullException(nameof(animationsEnabled));
        _pointerPosition = pointerPosition;
        _translationService = translationService;
    }

    public OverlayControllers Create(OverlayControllerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SelectionOverlayController? selection = null;
        ColorPickController? colorPick = null;
        ProviderMenuController? provider = null;
        MusicOverlayController? music = null;
        ToastOverlayController? toast = null;
        DebugOverlayController? debug = null;
        TextOverlayController? text = null;
        TranslationOverlayController? translation = null;
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
                _pointerPosition);
            provider = new ProviderMenuController(
                context.Visual.Provider,
                context.Visual.Bottom.Root,
                context.Providers,
                context.SelectedProviderId,
                context.Strings,
                context.Visual.LightTheme,
                context.CanUseProvider,
                context.ProviderSelected);
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

            var translationService = context.TranslationService ?? _translationService;
            if (context.Window is not null && context.SetMode is not null && translationService is not null)
            {
                text = new TextOverlayController(
                    context.Visual.TextSelection,
                    context.Visual.FloatingToolbar,
                    context.CoordinateRoot,
                    context.Window,
                    context.Strings,
                    _setClipboard,
                    toast.Show,
                    context.SelectionCompleted,
                    translationService,
                    context.GetMode,
                    context.SetMode,
                    context.Scale,
                    context.Overscan);

                translation = new TranslationOverlayController(
                    context.Visual.Translation,
                    context.FrozenFrame,
                    context.Scale,
                    translationService,
                    context.Strings,
                    _setClipboard,
                    toast.Show,
                    context.GetMode,
                    context.SetMode);
            }

            return new OverlayControllers(selection, colorPick, provider, music, toast, debug, text, translation);
        }
        catch
        {
            translation?.Dispose();
            text?.Dispose();
            music?.Dispose();
            debug?.Dispose();
            provider?.Dispose();
            selection?.Dispose();
            colorPick?.Dispose();
            toast?.Dispose();
            throw;
        }
    }
}
