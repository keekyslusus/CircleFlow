namespace CircleToSearch.Capture.OverlayInteractions;

using System.Windows;
using System.Windows.Input;
using CircleToSearch.Ocr;
using CircleToSearch.Translation;
using CircleToSearch.Ui;
using GdiRectangle = System.Drawing.Rectangle;

internal sealed class TextOverlayController : IDisposable
{
    private readonly TextSelectionVisual _visual;
    private readonly FloatingTextToolbarVisual _toolbar;
    private readonly FrameworkElement _coordinateRoot;
    private readonly Window _window;
    private readonly UiStrings _strings;
    private readonly Action<string> _setClipboard;
    private readonly Action<ToastNotification> _showToast;
    private readonly Action<GdiRectangle> _visualSearchRequested;
    private readonly ITranslationService _translationService;
    private readonly Func<OverlayInteractionMode> _getMode;
    private readonly Action<OverlayInteractionMode> _setMode;
    private readonly double _scale;
    private readonly bool _overscan;

    private OcrScreenSnapshot? _snapshot;
    private IReadOnlyList<OcrWordSnapshot> _selectedWords = [];
    private Point? _dragStart;
    private bool _selecting;
    private bool _disposed;

    public bool HasSelection => _selectedWords.Count > 0;

    public string SelectedText => string.Join(" ", _selectedWords.Select(w => w.Text));

    public TextOverlayController(
        TextSelectionVisual visual,
        FloatingTextToolbarVisual toolbar,
        FrameworkElement coordinateRoot,
        Window window,
        UiStrings strings,
        Action<string> setClipboard,
        Action<ToastNotification> showToast,
        Action<GdiRectangle> visualSearchRequested,
        ITranslationService translationService,
        Func<OverlayInteractionMode> getMode,
        Action<OverlayInteractionMode> setMode,
        double scale,
        bool overscan)
    {
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        _toolbar = toolbar ?? throw new ArgumentNullException(nameof(toolbar));
        _coordinateRoot = coordinateRoot ?? throw new ArgumentNullException(nameof(coordinateRoot));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _setClipboard = setClipboard ?? throw new ArgumentNullException(nameof(setClipboard));
        _showToast = showToast ?? throw new ArgumentNullException(nameof(showToast));
        _visualSearchRequested = visualSearchRequested ?? throw new ArgumentNullException(nameof(visualSearchRequested));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _getMode = getMode ?? throw new ArgumentNullException(nameof(getMode));
        _setMode = setMode ?? throw new ArgumentNullException(nameof(setMode));
        _scale = scale;
        _overscan = overscan;

        _toolbar.CopyButton.Click += OnCopyClicked;
        _toolbar.SearchButton.Click += OnSearchClicked;
        _toolbar.TranslateButton.Click += OnTranslateClicked;
    }

    public void SetSnapshot(OcrScreenSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public bool IsOverText(Point dipPoint) => _snapshot?.FindWordAt(dipPoint) is not null;

    public void UpdateHoverCursor(Point dipPoint)
    {
        if (_disposed || _selecting) return;
        var mode = _getMode();
        if (mode != OverlayInteractionMode.Selecting && mode != OverlayInteractionMode.TextSelection) return;

        if (IsOverText(dipPoint))
        {
            _window.Cursor = Cursors.IBeam;
        }
        else
        {
            _window.Cursor = Cursors.Cross;
        }
    }

    public bool StartSelection(Point dipPoint)
    {
        if (_disposed || _snapshot is null) return false;
        var word = _snapshot.FindWordAt(dipPoint);
        if (word is null) return false;

        _selecting = true;
        _dragStart = dipPoint;
        _selectedWords = [word];
        _visual.UpdateSelection(_selectedWords);
        _toolbar.Hide();
        _setMode(OverlayInteractionMode.TextSelection);
        return true;
    }

    public void UpdateDrag(Point currentDipPoint)
    {
        if (_disposed || !_selecting || _dragStart is null || _snapshot is null) return;
        _selectedWords = _snapshot.FindWordsInRange(_dragStart.Value, currentDipPoint);
        _visual.UpdateSelection(_selectedWords);
    }

    public void FinishDrag()
    {
        if (_disposed || !_selecting) return;
        _selecting = false;
        if (_selectedWords.Count > 0)
        {
            var union = _selectedWords.Select(w => w.DipRect).Aggregate(Rect.Union);
            _toolbar.ShowAt(union, new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight));
        }
        else
        {
            ClearSelection();
        }
    }

    public void ClearSelection()
    {
        _selecting = false;
        _selectedWords = [];
        _visual.Clear();
        _toolbar.Hide();
        if (_getMode() == OverlayInteractionMode.TextSelection)
        {
            _setMode(OverlayInteractionMode.Selecting);
        }
    }

    public bool TryCopySelection()
    {
        if (!HasSelection) return false;
        CopySelectedText();
        return true;
    }

    private void CopySelectedText()
    {
        var text = SelectedText;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            _setClipboard(text);
            _showToast(new ToastNotification(_strings.TextCopied, ToastTone.Success));
        }
        catch
        {
        }
        ClearSelection();
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        CopySelectedText();
    }

    private void OnSearchClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedWords.Count == 0) return;
        var union = _selectedWords.Select(w => w.DipRect).Aggregate(Rect.Union);
        var offset = _overscan ? 1d : 0d;
        var physical = new GdiRectangle(
            (int)Math.Round((union.X - offset) * _scale),
            (int)Math.Round((union.Y - offset) * _scale),
            (int)Math.Round(union.Width * _scale),
            (int)Math.Round(union.Height * _scale));
        ClearSelection();
        _visualSearchRequested(physical);
    }

    private async void OnTranslateClicked(object sender, RoutedEventArgs e)
    {
        var text = SelectedText;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var translated = await _translationService.TranslateTextAsync(text, string.Empty, CancellationToken.None).ConfigureAwait(true);
            if (!_disposed && !string.IsNullOrWhiteSpace(translated))
            {
                _setClipboard(translated);
                _showToast(new ToastNotification(translated, ToastTone.Success));
                ClearSelection();
            }
        }
        catch
        {
            if (!_disposed)
                _showToast(new ToastNotification(_strings.TranslationFailed, ToastTone.Error));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _toolbar.CopyButton.Click -= OnCopyClicked;
        _toolbar.SearchButton.Click -= OnSearchClicked;
        _toolbar.TranslateButton.Click -= OnTranslateClicked;
        ClearSelection();
    }
}
