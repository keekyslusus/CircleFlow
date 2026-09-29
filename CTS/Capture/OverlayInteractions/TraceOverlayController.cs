using System.Windows.Controls;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class TraceOverlayController : IDisposable
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
    private readonly Func<Uri, ITraceVideoPreview>? _createVideo;
    private TraceOverlayVisual? _visual;
    private bool _disposed;

    internal TraceOverlayController(
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
        Func<Uri, ITraceVideoPreview>? createVideo = null)
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
        _createVideo = createVideo;
    }

    internal bool TryStart(string providerId, GdiRectangle bounds)
    {
        if (_disposed || _publishCommand is null || providerId != SearchProviderIds.TraceMoe)
            return false;

        _transitionMode(OverlayInteractionMode.TraceLoading);
        _visual?.Dispose();

        TraceOverlayVisual? visual = null;
        visual = TraceOverlayVisual.Create(
            _root,
            _activityPresenter,
            _bottom,
            _effects,
            _strings,
            _isLightTheme(),
            () => OpenResult(visual),
            () => CloseResult(visual),
            _clipboardCopy,
            _createVideo);
        _visual = visual;

        var selection = _createSelectionCopy(bounds);
        try
        {
            _publishCommand(new VisualSelection(selection, SearchProviderIds.TraceMoe));
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
        if (_disposed || _getMode() != OverlayInteractionMode.TraceLoading) return;
        var visual = _visual;
        if (visual is null) return;
        _transitionMode(OverlayInteractionMode.TraceResult);
        if (!_disposed && ReferenceEquals(_visual, visual)) visual.ShowResult(outcome);
    }

    internal void DismissResult()
    {
        if (_disposed) return;
        _visual?.DismissResult();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var visual = _visual;
        _visual = null;
        visual?.Dispose();
    }

    private void OpenResult(TraceOverlayVisual? visual)
    {
        if (_disposed || !ReferenceEquals(_visual, visual)) return;
        _publishCommand?.Invoke(new OpenTraceResult());
    }

    private void CloseResult(TraceOverlayVisual? visual)
    {
        if (_disposed || !ReferenceEquals(_visual, visual) ||
            _getMode() != OverlayInteractionMode.TraceResult) return;
        _transitionMode(OverlayInteractionMode.Selecting);
    }
}
