using System.Windows;
using System.Windows.Media.Imaging;
using CircleToSearch.Links;
using CircleToSearch.QrCodes;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class QrCodeOverlayController : IDisposable
{
    private readonly QrCodeVisual _visual;
    private readonly BitmapSource _frame;
    private readonly FrameworkElement _coordinateRoot;
    private readonly OverlayCoordinateMapper _mapper;
    private readonly Func<BitmapSource, CancellationToken, IReadOnlyList<QrCodeMatch>>? _scan;
    private readonly ClipboardCopyService _clipboardCopy;
    private readonly Action<IOverlayCommand> _publish;
    private readonly UiStrings _strings;
    private readonly Func<bool> _animationsEnabled;
    private readonly PluginLog? _log;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<QrCodeChip> _chips = [];
    private bool _started;
    private bool _available = true;
    private bool _suppressed;
    private bool _shown;
    private bool _openPublished;
    private bool _disposed;

    // A null scan leaves the feature off for this overlay.
    internal QrCodeOverlayController(
        QrCodeVisual visual,
        BitmapSource frame,
        FrameworkElement coordinateRoot,
        OverlayCoordinateMapper mapper,
        Func<BitmapSource, CancellationToken, IReadOnlyList<QrCodeMatch>>? scan,
        ClipboardCopyService clipboardCopy,
        Action<IOverlayCommand> publish,
        UiStrings strings,
        Func<bool> animationsEnabled,
        PluginLog? log = null)
    {
        _visual = visual;
        _frame = frame;
        _coordinateRoot = coordinateRoot;
        _mapper = mapper;
        _scan = scan;
        _clipboardCopy = clipboardCopy;
        _publish = publish;
        _strings = strings;
        _animationsEnabled = animationsEnabled;
        _log = log;
    }

    internal void Start()
    {
        if (_started || _disposed || _scan is null) return;
        _started = true;
        _ = RunAsync(_scan, _cancellation.Token);
    }

    // Unavailable outside plain selection, e.g. while music, a widget or a translation owns the overlay.
    internal void SetAvailable(bool available)
    {
        _available = available;
        UpdateVisibility();
    }

    // Suppressed while a lasso or its selection is on screen, alongside the action tray.
    internal void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        UpdateVisibility();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        foreach (var chip in _chips)
        {
            chip.Primary.Click -= OnPrimary;
            if (chip.Copy is not null) chip.Copy.Click -= OnCopy;
            chip.Toolbar.Surface.MouseEnter -= OnChipHover;
            chip.Toolbar.Surface.MouseLeave -= OnChipHover;
            chip.Toolbar.Hide(animate: false);
        }
    }

    private async Task RunAsync(
        Func<BitmapSource, CancellationToken, IReadOnlyList<QrCodeMatch>> scan,
        CancellationToken cancellation)
    {
        IReadOnlyList<QrCodeMatch> matches;
        try
        {
            matches = await Task.Run(() => scan(_frame, cancellation), cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception exception)
        {
            _log?.Error(nameof(QrCodeOverlayController), "QR code scan failed unexpectedly.", exception);
            return;
        }

        // The content can hold Wi-Fi passwords or sign-in secrets, so only the count is logged.
        _log?.Info(nameof(QrCodeOverlayController), $"QR code scan found {matches.Count} code(s).");
        if (matches.Count == 0) return;
        var dispatcher = _coordinateRoot.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        try { await dispatcher.InvokeAsync(() => ShowMatches(matches)); }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) { }
        catch (Exception exception)
        {
            _log?.Error(nameof(QrCodeOverlayController), "Showing QR code chips failed unexpectedly.", exception);
        }
    }

    private void ShowMatches(IReadOnlyList<QrCodeMatch> matches)
    {
        if (_disposed) return;
        foreach (var match in matches)
        {
            var chip = QrCodeVisualFactory.CreateChip(
                _visual, ScreenLink.Classify(match.Text), _mapper.ToDips(match.Bounds), _strings);
            chip.Primary.Click += OnPrimary;
            if (chip.Copy is not null) chip.Copy.Click += OnCopy;
            chip.Toolbar.Surface.MouseEnter += OnChipHover;
            chip.Toolbar.Surface.MouseLeave += OnChipHover;
            _chips.Add(chip);
        }
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_disposed) return;
        var shown = _available && !_suppressed && !_openPublished && _chips.Count != 0;
        if (shown == _shown) return;
        _shown = shown;
        var animate = _animationsEnabled();
        var viewport = new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight);
        foreach (var chip in _chips)
        {
            if (shown)
            {
                chip.Toolbar.Show(chip.Viewfinder.Frame, viewport);
                chip.Viewfinder.Show(animate);
                continue;
            }
            chip.Toolbar.Hide(animate);
            chip.Viewfinder.Hide(animate);
            chip.Viewfinder.SetEmphasized(false, animate: false);
        }
    }

    // The brackets tell which code a chip belongs to when several are on screen.
    private void OnChipHover(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_disposed || _chips.FirstOrDefault(chip => ReferenceEquals(chip.Toolbar.Surface, sender)) is not { } chip)
            return;
        chip.Viewfinder.SetEmphasized(_shown && e.RoutedEvent == System.Windows.Input.Mouse.MouseEnterEvent,
            _animationsEnabled());
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Find(sender, chip => chip.Primary) is not { } chip) return;
        var link = chip.Link;
        switch (link.Kind)
        {
            case ScreenLinkKind.Web or ScreenLinkKind.Email when link.Target is not null:
                if (_openPublished) return;
                _openPublished = true;
                UpdateVisibility();
                _publish(new OpenLink(link.Target));
                break;
            case ScreenLinkKind.Wifi:
                Copied(chip, _clipboardCopy.TryCopy(link.CopyText, _strings.QrPasswordCopied));
                break;
            case ScreenLinkKind.Secret:
                Copied(chip, _clipboardCopy.TryCopy(link.CopyText, _strings.Copied));
                break;
            default:
                Copied(chip, _clipboardCopy.TryCopy(link.CopyText));
                break;
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Find(sender, chip => chip.Copy) is { } chip) Copied(chip, _clipboardCopy.TryCopy(chip.Link.CopyText));
    }

    private void Copied(QrCodeChip chip, bool copied)
    {
        if (copied) chip.Viewfinder.Pulse(_animationsEnabled());
    }

    private QrCodeChip? Find(object sender, Func<QrCodeChip, object?> button) =>
        _chips.FirstOrDefault(chip => ReferenceEquals(button(chip), sender));
}
