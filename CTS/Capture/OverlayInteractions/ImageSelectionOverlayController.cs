using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Ui;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class ImageSelectionOverlayController : IDisposable
{
    internal static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromMilliseconds(800);
    private readonly ImageSelectionVisual _visual;
    private readonly FrameworkElement _root;
    private readonly OverlayCoordinateMapper _mapper;
    private readonly SelectionOverlayController _selection;
    private readonly ScreenTranslationOverlayController _translation;
    private readonly ClipboardCopyService _clipboard;
    private readonly Func<BitmapSource> _visibleImage;
    private readonly Action<GdiRectangle> _search;
    private readonly Action<IOverlayCommand> _publish;
    private readonly Action _close;
    private readonly UiStrings _strings;
    private readonly DispatcherTimer _copyTimer;
    private readonly List<IDisposable> _ripples;
    private bool _disposed;

    internal ImageSelectionOverlayController(ImageSelectionVisual visual, FrameworkElement root,
        OverlayCoordinateMapper mapper, SelectionOverlayController selection,
        ScreenTranslationOverlayController translation, ClipboardCopyService clipboard,
        Func<BitmapSource> visibleImage, Action<GdiRectangle> search, Action<IOverlayCommand> publish,
        Action close, UiStrings strings)
    {
        _visual = visual;
        _root = root;
        _mapper = mapper;
        _selection = selection;
        _translation = translation;
        _clipboard = clipboard;
        _visibleImage = visibleImage;
        _search = search;
        _publish = publish;
        _close = close;
        _strings = strings;
        _copyTimer = new DispatcherTimer(DispatcherPriority.Normal, root.Dispatcher)
        { Interval = CopyFeedbackDuration };
        _copyTimer.Tick += OnCopyFeedbackCompleted;
        visual.SearchButton.Click += OnSearch;
        visual.CopyButton.Click += OnCopy;
        visual.SaveButton.Click += OnSave;
        visual.TranslateButton.Click += OnTranslate;
        _ripples = [.. OverlayVisualResources.AttachControlRipples(visual.Toolbar.Layer)];
    }

    internal GdiRectangle? Bounds { get; private set; }
    internal bool IsCompleting { get; private set; }

    internal void Select(GdiRectangle bounds)
    {
        if (_disposed) return;
        Bounds = bounds;
        IsCompleting = false;
        _translation.SetRegion(bounds);
        Refresh(OverlayInteractionMode.Selecting);
    }

    internal void Refresh(OverlayInteractionMode mode)
    {
        if (_disposed || Bounds is not { } bounds) return;
        if (mode is not (OverlayInteractionMode.Selecting or OverlayInteractionMode.TranslationShown))
        {
            _visual.Toolbar.Hide();
            return;
        }
        _selection.ShowSelectionFrame(bounds, hold: false);
        var label = _translation.IsTranslationShown ? _strings.ShowOriginal : _strings.Translate;
        _visual.TranslateButton.Content = label;
        AutomationProperties.SetName(_visual.TranslateButton, label);
        _visual.Toolbar.Show(_mapper.ToDips(bounds), new Size(_root.ActualWidth, _root.ActualHeight));
        _visual.Toolbar.Surface.IsEnabled = !IsCompleting;
    }

    internal void Dismiss()
    {
        if (Bounds is not null) _translation.CommitVisibleImage();
        Bounds = null;
        _copyTimer.Stop();
        IsCompleting = false;
        _visual.Toolbar.Hide();
    }

    private bool CanAct => !_disposed && !IsCompleting && Bounds is not null && _visual.Toolbar.IsOpen;

    private void OnSearch(object sender, RoutedEventArgs e)
    {
        if (!CanAct) return;
        IsCompleting = true;
        _search(Bounds!.Value);
        e.Handled = true;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!CanAct) return;
        if (_clipboard.TryCopyImage(VisibleImage.Crop(_visibleImage(), Bounds!.Value)))
        {
            IsCompleting = true;
            _visual.Toolbar.Surface.IsEnabled = false;
            _copyTimer.Start();
        }
        e.Handled = true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!CanAct) return;
        var image = VisibleImage.Crop(_visibleImage(), Bounds!.Value);
        IsCompleting = true;
        _visual.Toolbar.Hide();
        _publish(new SaveSelectedImage(image));
        e.Handled = true;
    }

    private void OnTranslate(object sender, RoutedEventArgs e)
    {
        if (!CanAct) return;
        _translation.Toggle();
        e.Handled = true;
    }

    private void OnCopyFeedbackCompleted(object? sender, EventArgs e)
    {
        _copyTimer.Stop();
        if (!_disposed) _close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Dismiss();
        _visual.Toolbar.Hide(animate: false);
        _copyTimer.Tick -= OnCopyFeedbackCompleted;
        _visual.SearchButton.Click -= OnSearch;
        _visual.CopyButton.Click -= OnCopy;
        _visual.SaveButton.Click -= OnSave;
        _visual.TranslateButton.Click -= OnTranslate;
        foreach (var ripple in _ripples) ripple.Dispose();
    }
}
