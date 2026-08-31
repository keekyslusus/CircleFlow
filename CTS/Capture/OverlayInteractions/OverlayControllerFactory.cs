using System.Windows;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal interface IOverlayControllerFactory
{
    OverlayControllers Create(OverlayControllerContext context);
}

internal sealed class OverlayControllers(
    SelectionOverlayController selection,
    ProviderMenuController provider,
    MusicOverlayController music) : IDisposable
{
    private bool _disposed;

    internal SelectionOverlayController Selection { get; } = selection;
    internal ProviderMenuController Provider { get; } = provider;
    internal MusicOverlayController Music { get; } = music;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Music.Dispose();
        Provider.Dispose();
        Selection.Dispose();
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
    bool DebugEnabled,
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
    Action<IOverlayCommand> MusicResultCommandRequested);

internal sealed class OverlayControllerFactory : IOverlayControllerFactory
{
    private readonly Action<string> _setClipboard;
    private readonly Func<bool> _animationsEnabled;

    internal OverlayControllerFactory()
        : this(Clipboard.SetText, OverlayVisualResources.AnimationsEnabled)
    {
    }

    internal OverlayControllerFactory(Action<string> setClipboard, Func<bool> animationsEnabled)
    {
        _setClipboard = setClipboard ?? throw new ArgumentNullException(nameof(setClipboard));
        _animationsEnabled = animationsEnabled ?? throw new ArgumentNullException(nameof(animationsEnabled));
    }

    public OverlayControllers Create(OverlayControllerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SelectionOverlayController? selection = null;
        ProviderMenuController? provider = null;
        MusicOverlayController? music = null;
        try
        {
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
                context.SelectionHoldCompleted);
            provider = new ProviderMenuController(
                context.Visual.Provider,
                context.Visual.Actions.Root,
                context.Providers,
                context.SelectedProviderId,
                context.Strings,
                context.Visual.LightTheme,
                context.CanUseProvider,
                context.ProviderSelected);
            music = new MusicOverlayController(
                context.Visual.Music,
                context.Visual.Effects,
                context.Visual.Root,
                context.Strings,
                context.Visual.LightTheme,
                context.DebugEnabled,
                context.GetMode,
                context.MusicStartRequested,
                context.MusicCancelRequested,
                context.DebugScenarioSelected,
                context.MusicResultCommandRequested,
                _setClipboard,
                _animationsEnabled);
            return new OverlayControllers(selection, provider, music);
        }
        catch
        {
            music?.Dispose();
            provider?.Dispose();
            selection?.Dispose();
            throw;
        }
    }
}
