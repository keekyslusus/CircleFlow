using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
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
    private readonly BitmapSource _frozenFrame;
    private readonly ISelectionTextRefiner _textRefiner;
    private readonly Action<string> _setClipboard;
    private readonly Action<ToastNotification> _showToast;
    private readonly Func<string> _selectedProviderId;
    private readonly Action<IOverlayCommand> _publish;
    private readonly UiStrings _strings;
    private readonly bool _lightTheme;
    private OcrDocument? _document;
    private OcrDocument? _gestureDocument;
    private OcrWord? _anchor;
    private OcrWord? _current;
    private OcrWord? _hovered;
    private TextSelectionRange? _selection;
    private bool _disposed;
    private bool _searchPublished;
    private bool _actionPending;
    private int _selectionGeneration;
    private int _resolvedGeneration = -1;
    private string? _resolvedText;
    private CancellationTokenSource? _refinementCancellation;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    internal TextSelectionOverlayController(
        TextSelectionVisual visual,
        FrameworkElement coordinateRoot,
        FrameworkElement inputSurface,
        OverlayCoordinateMapper mapper,
        OcrTextHitTester hitTester,
        BitmapSource frozenFrame,
        ISelectionTextRefiner textRefiner,
        Action<string> setClipboard,
        Action<ToastNotification> showToast,
        Func<string> selectedProviderId,
        Action<IOverlayCommand> publish,
        UiStrings strings,
        bool lightTheme)
    {
        _visual = visual;
        _coordinateRoot = coordinateRoot;
        _inputSurface = inputSurface;
        _mapper = mapper;
        _hitTester = hitTester;
        _frozenFrame = frozenFrame ?? throw new ArgumentNullException(nameof(frozenFrame));
        _textRefiner = textRefiner ?? throw new ArgumentNullException(nameof(textRefiner));
        _setClipboard = setClipboard;
        _showToast = showToast;
        _selectedProviderId = selectedProviderId;
        _publish = publish;
        _strings = strings;
        _lightTheme = lightTheme;
        _visual.CopyButton.Click += OnCopy;
        _visual.SearchButton.Click += OnSearch;
    }

    internal bool HasSelection => _selection is not null;
    internal bool IsActionMenuOpen => _visual.ActionCard.Visibility == Visibility.Visible;

    internal void SetDocument(OcrDocument? document)
    {
        InvalidateResolvedText();
        _document = document;
        _hovered = null;
        if (document is null) Dismiss();
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
        if (ReferenceEquals(word, _hovered)) return;
        _hovered = word;
        _coordinateRoot.Cursor = word is null ? Cursors.Cross : Cursors.IBeam;
        if (_selection is null) RenderHighlights(word is null ? [] : [word.BoundsPx], hover: true);
    }

    internal void Dismiss()
    {
        InvalidateResolvedText();
        ReleaseCapture();
        _gestureDocument = null;
        _anchor = null;
        _current = null;
        _selection = null;
        _hovered = null;
        _searchPublished = false;
        _actionPending = false;
        _visual.CopyButton.IsEnabled = true;
        _visual.SearchButton.IsEnabled = true;
        _visual.ActionCard.Visibility = Visibility.Collapsed;
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
        _lifetimeCancellation.Cancel();
        _refinementCancellation?.Cancel();
        _visual.CopyButton.Click -= OnCopy;
        _visual.SearchButton.Click -= OnSearch;
        Dismiss();
        _lifetimeCancellation.Dispose();
    }

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_selection is null || _actionPending || _disposed) return;
        var selection = _selection;
        var generation = _selectionGeneration;
        _actionPending = true;
        SetActionButtonsEnabled(false);
        var text = await ResolveSelectionTextAsync(selection, generation);
        if (_disposed) return;
        await InvokeOnUiAsync(() =>
        {
            if (_disposed || generation != _selectionGeneration) return;
            if (text is not null)
            {
                try
                {
                    _setClipboard(text);
                    _showToast(new ToastNotification(_strings.TextCopied, ToastTone.Success));
                }
                catch
                {
                    _showToast(new ToastNotification(_strings.TextCopyFailed, ToastTone.Error));
                }
            }
            if (!_disposed && generation == _selectionGeneration)
            {
                _actionPending = false;
                SetActionButtonsEnabled(true);
            }
        });
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_selection is null || _searchPublished || _actionPending || _disposed) return;
        var selection = _selection;
        var generation = _selectionGeneration;
        var providerId = _selectedProviderId();
        _actionPending = true;
        SetActionButtonsEnabled(false);
        var text = await ResolveSelectionTextAsync(selection, generation);
        if (_disposed) return;
        await InvokeOnUiAsync(() =>
        {
            if (_disposed || generation != _selectionGeneration) return;
            if (text is not null)
            {
                _searchPublished = true;
                _publish(new SearchSelectedText(text, providerId));
            }
            if (!_disposed && generation == _selectionGeneration)
            {
                _actionPending = false;
                _visual.CopyButton.IsEnabled = true;
                _visual.SearchButton.IsEnabled = !_searchPublished;
            }
        });
    }

    internal async Task<string?> ResolveSelectionTextAsync(TextSelectionRange selection, int generation)
    {
        if (_disposed || generation != _selectionGeneration) return null;
        if (_resolvedGeneration == generation && _resolvedText is not null) return _resolvedText;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _refinementCancellation = cancellation;
        SelectionTextRefinement refinement;
        try
        {
            refinement = await _textRefiner.RefineAsync(_frozenFrame, selection, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            refinement = SelectionTextRefinement.Failed();
        }
        finally
        {
            if (ReferenceEquals(_refinementCancellation, cancellation)) _refinementCancellation = null;
        }
        if (_disposed || cancellation.IsCancellationRequested || generation != _selectionGeneration) return null;
        if (refinement.Status == SelectionTextRefinementStatus.Canceled) return null;
        var resolved = refinement.Status == SelectionTextRefinementStatus.Success &&
                       !string.IsNullOrWhiteSpace(refinement.Text)
            ? refinement.Text
            : selection.Text;
        _resolvedGeneration = generation;
        _resolvedText = resolved;
        return resolved;
    }

    private void RenderRange(TextSelectionRange range, bool showMenu)
    {
        InvalidateResolvedText();
        _selection = range;
        _searchPublished = false;
        RenderHighlights(range.HighlightBoundsPx, hover: false);
        if (!showMenu) return;
        _visual.ActionCard.Visibility = Visibility.Visible;
        _visual.SearchButton.IsEnabled = true;
        _visual.CopyButton.IsEnabled = true;
        _visual.ActionCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var selected = _mapper.ToDips(range.BoundsPx);
        var desired = _visual.ActionCard.DesiredSize;
        var placement = TextActionCardLayout.Place(
            selected,
            desired,
            new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight));
        Canvas.SetLeft(_visual.ActionCard, placement.X);
        Canvas.SetTop(_visual.ActionCard, placement.Y);
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

    private void InvalidateResolvedText()
    {
        _selectionGeneration++;
        _resolvedGeneration = -1;
        _resolvedText = null;
        _refinementCancellation?.Cancel();
        _refinementCancellation = null;
        if (!_disposed)
        {
            _actionPending = false;
            SetActionButtonsEnabled(true);
        }
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        _visual.CopyButton.IsEnabled = enabled;
        _visual.SearchButton.IsEnabled = enabled && !_searchPublished;
    }

    private async Task InvokeOnUiAsync(Action action)
    {
        var dispatcher = _coordinateRoot.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        try
        {
            if (dispatcher.CheckAccess()) action();
            else await dispatcher.InvokeAsync(action);
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) { }
    }
}
