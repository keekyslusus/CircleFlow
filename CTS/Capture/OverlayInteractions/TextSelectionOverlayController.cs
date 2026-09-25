using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using CircleToSearch.Search;
using CircleToSearch.TextRecognition;
using CircleToSearch.Ui;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class TextSelectionOverlayController : IDisposable
{
    private readonly TextSelectionVisual _visual;
    private readonly FrameworkElement _coordinateRoot;
    private readonly FrameworkElement _inputSurface;
    private readonly OverlayCoordinateMapper _mapper;
    private readonly OcrTextHitTester _hitTester;
    private readonly ClipboardCopyService _clipboardCopy;
    private readonly Func<string> _selectedProviderId;
    private readonly Action<IOverlayCommand> _publish;
    private readonly UiStrings _strings;
    private readonly bool _lightTheme;
    private readonly Action<bool>? _textHoverChanged;
    private OcrDocument? _document;
    private OcrDocument? _gestureDocument;
    private OcrWord? _anchor;
    private OcrWord? _current;
    private OcrWord? _hovered;
    private TextSelectionRange? _selection;
    private bool _disposed;
    private bool _searchPublished;
    private bool _overText;

    internal TextSelectionOverlayController(
        TextSelectionVisual visual,
        FrameworkElement coordinateRoot,
        FrameworkElement inputSurface,
        OverlayCoordinateMapper mapper,
        OcrTextHitTester hitTester,
        ClipboardCopyService clipboardCopy,
        Func<string> selectedProviderId,
        Action<IOverlayCommand> publish,
        UiStrings strings,
        bool lightTheme,
        Action<bool>? textHoverChanged = null)
    {
        _visual = visual;
        _coordinateRoot = coordinateRoot;
        _inputSurface = inputSurface;
        _mapper = mapper;
        _hitTester = hitTester;
        _clipboardCopy = clipboardCopy ?? throw new ArgumentNullException(nameof(clipboardCopy));
        _selectedProviderId = selectedProviderId;
        _publish = publish;
        _strings = strings;
        _lightTheme = lightTheme;
        _textHoverChanged = textHoverChanged;
        _visual.CopyButton.Click += OnCopy;
        _visual.SearchButton.Click += OnSearch;
    }

    internal bool HasSelection => _selection is not null;
    internal bool IsActionMenuOpen => _visual.Toolbar.IsOpen;

    internal void SetDocument(OcrDocument? document)
    {
        _document = document;
        _hovered = null;
        if (document is null)
        {
            SetOverText(false);
            Dismiss();
        }
        else if (_selection is null) _visual.HighlightLayer.Children.Clear();
    }

    internal bool Begin(Point point)
    {
        if (_disposed || _document is null) return false;
        var word = _hitTester.HitTest(_document, _mapper.ToPhysical(point, clamp: false));
        if (word is null) return false;
        Dismiss();
        _gestureDocument = _document;
        _anchor = word;
        _current = word;
        RenderRange(TextSelectionRange.Create(_gestureDocument, word, word), showMenu: false);
        _coordinateRoot.Cursor = Cursors.IBeam;
        _inputSurface.CaptureMouse();
        return true;
    }

    internal void Update(Point point)
    {
        if (_gestureDocument is null || _anchor is null) return;
        var word = _hitTester.HitTest(_gestureDocument, _mapper.ToPhysical(point, clamp: false));
        if (word is null || ReferenceEquals(word, _current)) return;
        _current = word;
        RenderRange(TextSelectionRange.Create(_gestureDocument, _anchor, word), showMenu: false);
    }

    internal void Complete(Point point)
    {
        if (_gestureDocument is null || _anchor is null) return;
        Update(point);
        var range = TextSelectionRange.Create(_gestureDocument, _anchor, _current ?? _anchor);
        ReleaseCapture();
        _gestureDocument = null;
        _anchor = null;
        _current = null;
        RenderRange(range, showMenu: true);
    }

    internal void Hover(Point point)
    {
        if (_disposed || _gestureDocument is not null || IsActionMenuOpen) return;
        var word = _hitTester.HitTest(_document, _mapper.ToPhysical(point, clamp: false));
        SetOverText(word is not null);
        if (ReferenceEquals(word, _hovered)) return;
        _hovered = word;
        _coordinateRoot.Cursor = word is null ? Cursors.Cross : Cursors.IBeam;
        if (_selection is null) RenderHighlights(word is null ? [] : [word.BoundsPx], hover: true);
    }

    internal void EndHover()
    {
        if (_disposed || _gestureDocument is not null) return;
        SetOverText(false);
        if (_hovered is null) return;
        _hovered = null;
        if (_selection is null) RenderHighlights([], hover: true);
    }

    internal void Dismiss(bool animate = true)
    {
        ReleaseCapture();
        _gestureDocument = null;
        _anchor = null;
        _current = null;
        _selection = null;
        _hovered = null;
        _searchPublished = false;
        _visual.Toolbar.Hide(animate);
        _visual.HighlightLayer.Children.Clear();
    }

    internal void CancelGesture()
    {
        if (_gestureDocument is null) return;
        Dismiss();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visual.CopyButton.Click -= OnCopy;
        _visual.SearchButton.Click -= OnSearch;
        Dismiss(animate: false);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_selection is null) return;
        _clipboardCopy.TryCopy(_selection.Text);
        e.Handled = true;
    }

    private void OnSearch(object sender, RoutedEventArgs e)
    {
        if (_selection is null || _searchPublished) return;
        _searchPublished = true;
        _visual.SearchButton.IsEnabled = false;
        _publish(new SearchSelectedText(_selection.Text, _selectedProviderId()));
        e.Handled = true;
    }

    private void RenderRange(TextSelectionRange range, bool showMenu)
    {
        _selection = range;
        _searchPublished = false;
        RenderHighlights(range.HighlightBoundsPx, hover: false);
        if (!showMenu) return;
        _visual.SearchButton.IsEnabled = true;
        _visual.Toolbar.Show(
            _mapper.ToDips(range.BoundsPx),
            new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight));
    }

    private void RenderHighlights(IReadOnlyList<GdiRectangle> rectangles, bool hover)
    {
        _visual.HighlightLayer.Children.Clear();
        var color = hover
            ? PluginPalette.For(_lightTheme).TextInteraction.Hover
            : PluginPalette.For(_lightTheme).TextInteraction.Selection;
        foreach (var bounds in rectangles)
        {
            var dips = _mapper.ToDips(bounds);
            var rectangle = new Rectangle
            {
                Width = dips.Width,
                Height = dips.Height,
                Fill = OverlayVisualResources.Frozen(color),
                RadiusX = 2,
                RadiusY = 2,
            };
            Canvas.SetLeft(rectangle, dips.Left);
            Canvas.SetTop(rectangle, dips.Top);
            _visual.HighlightLayer.Children.Add(rectangle);
        }
    }

    private void ReleaseCapture()
    {
        if (ReferenceEquals(Mouse.Captured, _inputSurface)) Mouse.Capture(null);
    }

    private void SetOverText(bool overText)
    {
        if (_overText == overText) return;
        _overText = overText;
        _textHoverChanged?.Invoke(overText);
    }
}
