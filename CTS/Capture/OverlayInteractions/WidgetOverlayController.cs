using System.Windows.Controls;
using System.Windows.Input;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class WidgetOverlayController : IDisposable
{
    private readonly Grid _root;
    private readonly OverlayActivityPresenter _activityPresenter;
    private readonly BottomOverlayVisual _bottom;
    private readonly OverlayEffectsVisual _effects;
    private readonly UiStrings _strings;
    private readonly Func<bool> _isLightTheme;
    private readonly ClipboardCopyService _clipboardCopy;
    private readonly Func<OverlayInteractionMode> _getMode;
    private readonly Action<OverlayInteractionMode> _transitionMode;
    private readonly Action<IOverlayCommand>? _publishCommand;
    private readonly Func<GdiRectangle, SelectionOutcome> _createSelectionCopy;
    private readonly IReadOnlyDictionary<string, Func<OverlayWidgetContext, IOverlayWidgetVisual>> _visuals;
    private IOverlayWidgetVisual? _visual;
    private bool _disposed;

    internal WidgetOverlayController(
        Grid root,
        OverlayActivityPresenter activityPresenter,
        BottomOverlayVisual bottom,
        OverlayEffectsVisual effects,
        UiStrings strings,
        Func<bool> isLightTheme,
        ClipboardCopyService clipboardCopy,
        Func<OverlayInteractionMode> getMode,
        Action<OverlayInteractionMode> transitionMode,
        Action<IOverlayCommand>? publishCommand,
        Func<GdiRectangle, SelectionOutcome> createSelectionCopy,
        IReadOnlyDictionary<string, Func<OverlayWidgetContext, IOverlayWidgetVisual>> visuals)
    {
        _root = root;
        _activityPresenter = activityPresenter;
        _bottom = bottom;
        _effects = effects;
        _strings = strings;
        _isLightTheme = isLightTheme;
        _clipboardCopy = clipboardCopy;
        _getMode = getMode;
        _transitionMode = transitionMode;
        _publishCommand = publishCommand;
        _createSelectionCopy = createSelectionCopy;
        _visuals = visuals;
    }

    internal bool TryStart(string providerId, GdiRectangle bounds)
    {
        if (_disposed || _publishCommand is null || !_visuals.TryGetValue(providerId, out var createVisual))
            return false;

        _transitionMode(OverlayInteractionMode.WidgetLoading);
        _visual?.Dispose();

        IOverlayWidgetVisual? visual = null;
        visual = createVisual(new OverlayWidgetContext(
            _root,
            _activityPresenter,
            _bottom,
            _effects,
            _strings,
            _isLightTheme(),
            url => OpenResult(visual, url),
            () => CloseResult(visual),
            _clipboardCopy));
        _visual = visual;

        var selection = _createSelectionCopy(bounds);
        try
        {
            _publishCommand(new VisualSelection(selection, providerId));
        }
        catch
        {
            selection.Dispose();
            throw;
        }
        return true;
    }

    internal void ShowResult(VisualSearchPreparationOutcome outcome)
    {
        if (_disposed || _getMode() != OverlayInteractionMode.WidgetLoading) return;
        var visual = _visual;
        if (visual is null) return;
        _transitionMode(OverlayInteractionMode.WidgetResult);
        if (!_disposed && ReferenceEquals(_visual, visual)) visual.ShowResult(outcome);
    }

    internal void DismissResult()
    {
        if (_disposed) return;
        _visual?.DismissResult();
    }

    internal bool TryGoBack() =>
        !_disposed && _getMode() == OverlayInteractionMode.WidgetResult && _visual?.TryGoBack() == true;

    internal bool TryHandleShortcut(Key key, ModifierKeys modifiers) =>
        !_disposed && _getMode() == OverlayInteractionMode.WidgetResult &&
        OverlayShortcuts.Copy.Matches(key, modifiers) && _visual?.TryCopy() == true;

    internal bool TryCloseResult()
    {
        if (_disposed || _getMode() != OverlayInteractionMode.WidgetResult) return false;
        _transitionMode(OverlayInteractionMode.Selecting);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var visual = _visual;
        _visual = null;
        visual?.Dispose();
    }

    private void OpenResult(IOverlayWidgetVisual? visual, Uri url)
    {
        if (_disposed || !ReferenceEquals(_visual, visual)) return;
        _publishCommand?.Invoke(new OpenWidgetResult(url));
    }

    private void CloseResult(IOverlayWidgetVisual? visual)
    {
        if (ReferenceEquals(_visual, visual)) TryCloseResult();
    }
}
