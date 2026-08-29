using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Interop;
using CircleToSearch.Ui;
using CircleToSearch.Search;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Ui.Effects;
using GdiBitmap = System.Drawing.Bitmap;
using GdiGraphics = System.Drawing.Graphics;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Capture;

public enum OverlayExitFade
{
    // Window-level opacity: turns the window layered mid-flight, which composites black
    // on some setups (reproduced in FadeCaptureTests). Kept only for the capture harness.
    Window,

    // Window created with AllowsTransparency; only the root grid fades. Production default.
    Root,

    // Opaque window; dim/lasso/chip fade away, the frozen frame stays until close.
    DimLayers,
}

internal enum OverlayInteractionMode
{
    Selecting,
    Listening,
    MusicResult,
}

public sealed class OverlayWindow : Window
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorEffectiveDpi = 0;
    private const double ChipEdgeMarginDips = 32;
    private const double SampleDistanceDips = 3;
    private static readonly TimeSpan ExitFadeDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan SelectionHoldDuration = TimeSpan.FromMilliseconds(450);

    private readonly GdiBitmap _frame;
    private readonly GdiRectangle _monitor;
    private readonly double _scale;
    private readonly int _paddingPx;
    private readonly int _minDiagonalPx;
    private readonly OverlayVisual _visual;
    private readonly LassoPathSampler _sampler;
    private readonly OverlayExitFade _exitFade;
    private readonly bool _clickThroughOnCancel;
    private readonly bool _overscan;
    private readonly Point? _entranceOrigin;
    private readonly UiStrings _strings;
    private readonly IReadOnlyList<SearchProviderDescriptor> _providers;
    private readonly Action<IOverlayCommand>? _publishCommand;
    private readonly List<IDisposable> _controlRipples = [];
    private string _selectedProviderId;
    private readonly List<Point> _stroke = [];
    private bool _drawing;
    private bool _finished;
    private bool _chipDismissed;
    private bool _revealUpdateQueued;
    private bool _providerMenuOpen;
    private bool _debugPanelOpen;
    private bool _cancelPublished;
    private bool _entranceRipplePending;

    internal OverlayInteractionMode Mode { get; private set; } = OverlayInteractionMode.Selecting;

    internal bool FrameTransferred { get; private set; }

    internal OverlayVisual VisualState => _visual;

    public OverlayOutcome? Outcome { get; private set; }

    internal OverlayWindow(
        GdiBitmap frame,
        GdiRectangle monitor,
        GdiRectangle workArea,
        double scale,
        OverlayOptions options,
        UiStrings strings,
        bool allowsTransparency = true,
        OverlayExitFade exitFade = OverlayExitFade.Root,
        bool clickThroughOnCancel = true,
        bool overscan = true,
        GdiPoint? entranceOrigin = null,
        IReadOnlyList<SearchProviderDescriptor>? providers = null,
        string? initialProviderId = null,
        Action<IOverlayCommand>? publishCommand = null)
    {
        _frame = frame;
        _monitor = monitor;
        _scale = scale;
        _paddingPx = options.PaddingPx;
        _minDiagonalPx = options.MinDiagonalPx;
        _exitFade = exitFade;
        _clickThroughOnCancel = clickThroughOnCancel;
        _overscan = overscan;
        _entranceOrigin = entranceOrigin is null
            ? null
            : new Point(
                (entranceOrigin.Value.X - monitor.Left) / scale + (overscan ? 1 : 0),
                (entranceOrigin.Value.Y - monitor.Top) / scale + (overscan ? 1 : 0));
        _strings = strings;
        _providers = providers ?? [];
        _selectedProviderId = initialProviderId ?? string.Empty;
        _publishCommand = publishCommand;
        _sampler = new LassoPathSampler(SampleDistanceDips * scale);

        Title = strings.PluginTitle;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        Cursor = Cursors.Cross;
        Left = monitor.Left / scale;
        Top = monitor.Top / scale;
        Width = monitor.Width / scale;
        Height = monitor.Height / scale;
        // Born transparent: a window that becomes layered later (window Opacity < 1) renders
        // black on this setup; a transparent window fades its content cleanly into the desktop.
        if (allowsTransparency)
        {
            AllowsTransparency = true;
            Background = CreateFrozenSolidBrush(PluginPalette.Transparent);
        }
        else
        {
            Background = CreateFrozenSolidBrush(PluginPalette.OpaqueBlack);
        }
        if (overscan)
        {
            // The window rect then differs from the monitor rect, which keeps DWM's
            // fullscreen-cover heuristics (global shadow disabling) idle; the 1 DIP
            // overhang on each side hangs off the screen and is never visible.
            Left -= 1;
            Top -= 1;
            Width += 2;
            Height += 2;
        }

        var frameSource = CreateFrozenFrame(frame);
        var visualSize = new Size(Width, Height);
        var bottomMargin = ChipBottomMargin(monitor, workArea, scale) + (overscan ? 1 : 0);
        _visual = _providers.Count == 0
            ? OverlayVisualFactory.CreateRoot(frameSource, visualSize, bottomMargin, strings)
            : OverlayVisualFactory.CreateRoot(
                frameSource,
                visualSize,
                bottomMargin,
                SystemTheme.IsLight(),
                strings,
                _providers,
                _selectedProviderId);
        if (overscan) _visual.Screenshot.Margin = new Thickness(1);
        Content = new AdornerDecorator { Child = _visual.Root };
        Loaded += OnLoaded;

        PreviewKeyDown += OnPreviewKeyDown;
        _visual.MusicButton.Click += OnMusicButtonClick;
        AttachDebugScenarioHandlers();
        if (_visual.ProviderButton is not null) _visual.ProviderButton.Click += OnProviderButtonClick;
        AttachProviderMenuHandlers();
        _visual.SelectionInputSurface.MouseLeftButtonDown += OnMouseLeftButtonDown;
        _visual.SelectionInputSurface.MouseMove += OnMouseMove;
        _visual.SelectionInputSurface.MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonDown += OnMouseRightButtonDown;
        Deactivated += OnDeactivated;
        Closed += (_, _) => DisposeVisualResources();
        Dispatcher.ShutdownStarted += (_, _) => DisposeVisualResources();
    }

    internal OverlayWindow(
        GdiBitmap frame,
        GdiRectangle monitor,
        GdiRectangle workArea,
        double scale,
        OverlayLaunchOptions options,
        Action<IOverlayCommand> publishCommand,
        bool allowsTransparency = true,
        OverlayExitFade exitFade = OverlayExitFade.Root,
        bool clickThroughOnCancel = true,
        bool overscan = true,
        GdiPoint? entranceOrigin = null)
        : this(
            frame,
            monitor,
            workArea,
            scale,
            options.CaptureOptions,
            options.Strings,
            allowsTransparency,
            exitFade,
            clickThroughOnCancel,
            overscan,
            entranceOrigin,
            options.Providers,
            options.InitialProviderId,
            publishCommand)
    {
    }

    // Must not be called from an MTA thread: it creates the STA thread that owns the overlay.
    // A null outcome means the selection was canceled.
    public static Task<OverlayOutcome?> SelectAsync(
        PluginLog log,
        OverlayOptions options,
        UiStrings strings,
        CancellationToken cancel)
    {
        var completion = new TaskCompletionSource<OverlayOutcome?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(RunOnce(log, options, strings, cancel));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "CircleToSearch overlay",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public void CancelFromCoordinator() => Dispatcher.BeginInvoke(new Action(() => CancelInternal()));

    private static OverlayOutcome? RunOnce(
        PluginLog log,
        OverlayOptions options,
        UiStrings strings,
        CancellationToken cancel)
    {
        var previousContext = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);
        try
        {
            if (!TryCapturePointerMonitor(out var monitor, out var workArea, out var frame, out var scale, out var pointer))
            {
                log.Warn(nameof(OverlayWindow), "capturing the pointer monitor failed; selection canceled");
                return null;
            }

            if (cancel.IsCancellationRequested)
            {
                frame.Dispose();
                return null;
            }

            OverlayOutcome? outcome = null;
            try
            {
                var window = new OverlayWindow(frame, monitor, workArea, scale, options, strings, entranceOrigin: pointer);
                window.Show();
                using var registration = cancel.Register(window.CancelFromCoordinator);
                Dispatcher.Run();
                outcome = window.Outcome;
                return outcome;
            }
            finally
            {
                if (outcome?.Action != OverlayAction.VisualSelection) frame.Dispose();
            }
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previousContext);
        }
    }

    internal static bool TryCapturePointerMonitor(
        out GdiRectangle monitor,
        out GdiRectangle workArea,
        out GdiBitmap frame,
        out double scale,
        out GdiPoint pointer)
    {
        monitor = default;
        workArea = default;
        frame = null!;
        scale = 1.0;
        pointer = default;

        if (!NativeMethods.GetCursorPos(out var nativePointer)) return false;
        pointer = new GdiPoint(nativePointer.X, nativePointer.Y);
        var handle = NativeMethods.MonitorFromPoint(nativePointer, MonitorDefaultToNearest);
        if (handle == IntPtr.Zero) return false;

        var info = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(handle, ref info)) return false;

        var bounds = info.Monitor;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return false;

        if (NativeMethods.GetDpiForMonitor(handle, MonitorEffectiveDpi, out var dpiX, out _) != 0 || dpiX == 0)
            dpiX = 96;
        scale = dpiX / 96.0;

        var captured = new GdiBitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = GdiGraphics.FromImage(captured);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new GdiSize(width, height));
        }
        catch
        {
            captured.Dispose();
            throw;
        }

        var work = info.Work;
        monitor = new GdiRectangle(bounds.Left, bounds.Top, width, height);
        workArea = new GdiRectangle(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        frame = captured;
        return true;
    }

    private static BitmapSource CreateFrozenFrame(GdiBitmap frame)
    {
        var hbmp = frame.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hbmp,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(hbmp);
        }
    }

    private static SolidColorBrush CreateFrozenSolidBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static double ChipBottomMargin(GdiRectangle monitor, GdiRectangle workArea, double scale) =>
        (monitor.Bottom - workArea.Bottom) / scale + ChipEdgeMarginDips;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_chipDismissed) OverlayVisualFactory.BeginChipEntrance(_visual);
        QueueEntranceRipple();
        _controlRipples.AddRange(OverlayVisualFactory.AttachControlRipples(_visual));
    }

    private void QueueEntranceRipple()
    {
        if (_entranceOrigin is null || _entranceRipplePending) return;
        _entranceRipplePending = true;
        CompositionTarget.Rendering += EmitEntranceRippleOnFirstFrame;
    }

    private void EmitEntranceRippleOnFirstFrame(object? sender, EventArgs e)
    {
        if (_finished)
        {
            UnqueueEntranceRipple();
            return;
        }
        if (_visual.SceneRippleLayer.ActualWidth <= 0 || _visual.SceneRippleLayer.ActualHeight <= 0) return;
        UnqueueEntranceRipple();
        _visual.SceneRipples.Emit(new SceneRippleRequest(
            _entranceOrigin!.Value,
            SceneRipplePreset.Entrance,
            1));
    }

    private void UnqueueEntranceRipple()
    {
        if (!_entranceRipplePending) return;
        _entranceRipplePending = false;
        CompositionTarget.Rendering -= EmitEntranceRippleOnFirstFrame;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_publishCommand is not null &&
            e.Key == Key.D &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            SetDebugPanelOpen(!_debugPanelOpen);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape) return;
        if (_debugPanelOpen)
        {
            SetDebugPanelOpen(false);
            e.Handled = true;
            return;
        }
        if (_providerMenuOpen)
        {
            SetProviderMenuOpen(false);
            e.Handled = true;
            return;
        }
        CancelInternal();
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var actionInteraction = IsActionTrayInteraction(e.OriginalSource, e.GetPosition(this));
        if (_providerMenuOpen && !actionInteraction)
        {
            SetProviderMenuOpen(false);
            e.Handled = true;
            return;
        }
        if (_finished || Mode != OverlayInteractionMode.Selecting || actionInteraction) return;
        _drawing = true;
        _chipDismissed = true;
        OverlayVisualFactory.BeginChipExit(_visual);
        _sampler.Reset();
        _stroke.Clear();
        Track(e);
        _visual.SelectionInputSurface.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _finished || Mode != OverlayInteractionMode.Selecting) return;
        Track(e);
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing || _finished || Mode != OverlayInteractionMode.Selecting) return;
        _drawing = false;
        ReleaseSelectionInput();
        Track(e, final: true);
        var bounds = LassoBoundsCalculator.Calculate(_sampler.Points, _monitor, _paddingPx, _minDiagonalPx);
        if (bounds is null)
        {
            CancelInternal();
        }
        else
        {
            _finished = true;
            var selection = new SelectionOutcome(bounds.Value, _frame);
            FrameTransferred = true;
            if (_publishCommand is null) Outcome = OverlayOutcome.VisualSelection(selection);
            else _publishCommand(new VisualSelection(selection, _selectedProviderId));
            ShowSelectionFrame(bounds.Value);
        }
        e.Handled = true;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelInternal();
        e.Handled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_finished || IsActive) return;
            if (IsActionTrayInteraction(null, Mouse.GetPosition(this))) return;
            CancelInternal();
        }, DispatcherPriority.ContextIdle);
    }

    private void OnProviderButtonClick(object sender, RoutedEventArgs e)
    {
        if (_finished) return;
        SetProviderMenuOpen(!_providerMenuOpen);
        e.Handled = true;
    }

    private void AttachProviderMenuHandlers()
    {
        if (_visual.ProviderMenu.Child is not StackPanel panel) return;
        foreach (var item in panel.Children.OfType<Button>()) item.Click += OnProviderMenuItemClick;
    }

    private void OnProviderMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_finished || sender is not Button { Tag: string providerId }) return;
        var descriptor = _providers.FirstOrDefault(provider =>
            string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null) return;
        _selectedProviderId = descriptor.Id;
        OverlayVisualFactory.UpdateProvider(
            _visual,
            _providers,
            _selectedProviderId,
            _strings,
            SystemTheme.IsLight());
        AttachProviderMenuHandlers();
        SetProviderMenuOpen(false);
        _publishCommand?.Invoke(new ProviderSelected(_selectedProviderId));
        e.Handled = true;
    }

    private void AttachDebugScenarioHandlers()
    {
        foreach (var button in _visual.DebugScenarioButtons.Children.OfType<Button>())
            button.Click += OnDebugScenarioClick;
    }

    private void OnDebugScenarioClick(object sender, RoutedEventArgs e)
    {
        if (_finished || sender is not Button { Tag: MusicDebugScenario scenario }) return;
        OverlayVisualFactory.SetMusicDebugScenario(_visual, scenario);
        _publishCommand?.Invoke(new MusicDebugScenarioSelected(scenario));
    }

    internal void SetDebugPanelOpen(bool open)
    {
        if (_finished || _publishCommand is null) return;
        _debugPanelOpen = open;
        _visual.DebugPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open) SetProviderMenuOpen(false);
    }

    private void SetProviderMenuOpen(bool open)
    {
        _providerMenuOpen = open;
        OverlayVisualFactory.SetProviderMenuOpen(_visual, open);
    }

    private void OnMusicButtonClick(object sender, RoutedEventArgs e)
    {
        if (_finished) return;
        if (_publishCommand is not null)
        {
            if (Mode == OverlayInteractionMode.Selecting)
            {
                SetDebugPanelOpen(false);
                ShowListening();
                _publishCommand(new StartMusicRecognition());
            }
            else
            {
                PublishCancel();
                CancelInternal(publish: false);
            }
            e.Handled = true;
            return;
        }
        _finished = true;
        _drawing = false;
        UnqueueRevealUpdate();
        ReleaseSelectionInput();
        Outcome = OverlayOutcome.MusicRecognition();
        IsHitTestVisible = false;
        Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        e.Handled = true;
    }

    internal bool IsActionTrayInteraction(object? originalSource, Point windowPoint)
    {
        var hit = InputHitTest(windowPoint) as DependencyObject;
        return IsWithin(originalSource as DependencyObject, _visual.ActionUiRoot) ||
               IsWithin(originalSource as DependencyObject, _visual.ResultHost) ||
               IsWithin(originalSource as DependencyObject, _visual.DebugPanel) ||
               IsWithin(hit, _visual.ActionUiRoot) ||
               IsWithin(hit, _visual.ResultHost) ||
               IsWithin(hit, _visual.DebugPanel) ||
               _visual.ActionUiRoot.IsMouseOver ||
               _visual.ResultHost.IsMouseOver ||
               _visual.DebugPanel.IsMouseOver;
    }

    internal void ShowListening()
    {
        if (_finished || Mode == OverlayInteractionMode.Listening) return;
        Mode = OverlayInteractionMode.Listening;
        _drawing = false;
        UnqueueRevealUpdate();
        ReleaseSelectionInput();
        _visual.ResultHost.Visibility = Visibility.Collapsed;
        OverlayVisualFactory.SetListeningState(_visual, listening: true);
        _visual.Waveform.Start();
        _visual.MusicButton.ToolTip = _strings.CancelMusicRecognition;
        System.Windows.Automation.AutomationProperties.SetName(
            _visual.MusicButton,
            _strings.CancelMusicRecognition);
        Cursor = Cursors.Arrow;
        FadeSelectionLayersForListening();
    }

    internal void ReportAudio(MusicVisualizationFrame frame)
    {
        if (_finished || Mode != OverlayInteractionMode.Listening) return;
        _visual.Waveform.Report(frame);
        if (!frame.IsTransient || !OverlayVisualFactory.AnimationsEnabled()) return;
        var center = _visual.Waveform.TransformToAncestor(_visual.Root).Transform(
            new Point(_visual.Waveform.ActualWidth / 2, _visual.Waveform.ActualHeight / 2));
        _visual.SceneRipples.Emit(new SceneRippleRequest(
            center,
            SceneRipplePreset.AudioTransient,
            frame.NormalizedPeak));
    }

    internal void ShowMusicResult(MusicRecognitionOutcome outcome)
    {
        if (_finished || outcome.Status == MusicRecognitionStatus.Canceled) return;
        Mode = OverlayInteractionMode.MusicResult;
        _visual.Waveform.Stop();
        OverlayVisualFactory.SetListeningState(_visual, listening: false);
        var card = OverlayVisualFactory.PresentMusicResult(
            _visual,
            outcome,
            _strings,
            command =>
            {
                if (command is DismissMusicResult) DismissMusicResult();
                _publishCommand?.Invoke(command);
            },
            CopyTrackInfo);
        _controlRipples.AddRange(OverlayVisualFactory.AttachControlRipples(_visual.ResultHost));
        if (outcome.Status == MusicRecognitionStatus.Matched && OverlayVisualFactory.AnimationsEnabled())
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_finished || Mode != OverlayInteractionMode.MusicResult) return;
                card.UpdateLayout();
                var origin = card.TransformToAncestor(_visual.Root).Transform(
                    new Point(card.ActualWidth / 2, card.ActualHeight / 2));
                _visual.SceneRipples.Emit(new SceneRippleRequest(origin, SceneRipplePreset.MusicMatch, 1));
            }, DispatcherPriority.Loaded);
        }
    }

    private void DismissMusicResult()
    {
        if (_finished || Mode != OverlayInteractionMode.MusicResult) return;
        _visual.ResultHost.Visibility = Visibility.Collapsed;
        _visual.ResultHost.Children.Clear();
        Mode = OverlayInteractionMode.Selecting;
        RestoreSelectionLayersAfterMusic();
        _visual.MusicButton.ToolTip = _strings.MusicRecognitionAction;
        AutomationProperties.SetName(_visual.MusicButton, _strings.MusicRecognitionAction);
        Cursor = Cursors.Cross;
    }

    internal void CloseFromSession()
    {
        _finished = true;
        _drawing = false;
        ReleaseSelectionInput();
        DisposeVisualResources();
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void ReleaseSelectionInput()
    {
        if (ReferenceEquals(Mouse.Captured, _visual.SelectionInputSurface))
            Mouse.Capture(null);
    }

    private void Track(MouseEventArgs e, bool final = false)
    {
        var dip = e.GetPosition(this);
        var physical = ToPhysical(dip);
        var accepted = final ? _sampler.AddFinal(physical) : _sampler.Add(physical);
        if (!accepted) return;
        _stroke.Add(dip);
        _visual.Halo.Points.Add(dip);
        _visual.Accent.Points.Add(dip);
        QueueRevealUpdate();
    }

    private void FadeSelectionLayersForListening()
    {
        UIElement[] fadeTargets =
        [
            _visual.Screenshot,
            _visual.Sheen,
            _visual.Halo,
            _visual.Accent,
            _visual.SelectionFrame,
        ];
        if (!OverlayVisualFactory.AnimationsEnabled())
        {
            foreach (var target in fadeTargets) target.Opacity = 0;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(200);
        foreach (var target in fadeTargets)
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(target.Opacity, 0, duration)
            {
                EasingFunction = EaseOut(),
            });
    }

    private void RestoreSelectionLayersAfterMusic()
    {
        UIElement[] restoreTargets =
        [
            _visual.Screenshot,
            _visual.Sheen,
            _visual.Halo,
            _visual.Accent,
        ];
        _visual.SelectionFrame.BeginAnimation(OpacityProperty, null);
        _visual.SelectionFrame.Opacity = 0;
        if (!OverlayVisualFactory.AnimationsEnabled())
        {
            foreach (var target in restoreTargets) target.Opacity = 1;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(200);
        foreach (var target in restoreTargets)
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(target.Opacity, 1, duration)
            {
                EasingFunction = EaseOut(),
            });
    }

    private void CopyTrackInfo(string text, Button button)
    {
        try
        {
            Clipboard.SetText(text);
            OverlayVisualFactory.SetCopyConfirmed(button, confirmed: true, _strings, _visual.LightTheme);
            var restore = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            restore.Tick += (_, _) =>
            {
                restore.Stop();
                if (_finished) return;
                OverlayVisualFactory.SetCopyConfirmed(button, confirmed: false, _strings, _visual.LightTheme);
            };
            restore.Start();
        }
        catch
        {
            OverlayVisualFactory.SetCopyConfirmed(button, confirmed: false, _strings, _visual.LightTheme);
        }
    }

    private void QueueRevealUpdate()
    {
        if (_revealUpdateQueued) return;
        _revealUpdateQueued = true;
        CompositionTarget.Rendering += FlushReveal;
    }

    private void UnqueueRevealUpdate()
    {
        if (!_revealUpdateQueued) return;
        _revealUpdateQueued = false;
        CompositionTarget.Rendering -= FlushReveal;
    }

    // Runs at most once per render frame, so fast drags never rebuild the mask more often than displayed.
    private void FlushReveal(object? sender, EventArgs e)
    {
        _revealUpdateQueued = false;
        CompositionTarget.Rendering -= FlushReveal;
        var size = new Size(ActualWidth, ActualHeight);
        _visual.Dim.Data = OverlayVisualFactory.BuildRevealGeometry(size, _stroke);
        _visual.Sheen.Data = OverlayVisualFactory.BuildPolygonGeometry(_stroke);
    }

    // Circle-to-search style finish: the lasso snaps into the exact rectangle that will be
    // sent, the frame holds for a beat so the region stays readable, then the window closes
    // into the provider with no fade (the frozen frame matches the live desktop).
    private void ShowSelectionFrame(GdiRectangle bounds)
    {
        UnqueueRevealUpdate();
        var size = new Size(ActualWidth, ActualHeight);
        var offset = _overscan ? 1 : 0;
        var rect = new Rect(
            bounds.Left / _scale + offset,
            bounds.Top / _scale + offset,
            bounds.Width / _scale,
            bounds.Height / _scale);
        Point[] corners =
        [
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        ];
        OverlayVisualFactory.BeginSelectionReveal(
            _visual,
            OverlayVisualFactory.BuildRevealGeometry(size, corners),
            OverlayVisualFactory.BuildSelectionFrameGeometry(rect));

        var hold = new DispatcherTimer { Interval = SelectionHoldDuration };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            FinishShutdown();
        };
        hold.Start();
    }

    private GdiPoint ToPhysical(Point dip)
        => new((int)Math.Round(dip.X * _scale), (int)Math.Round(dip.Y * _scale));

    private void CancelInternal(bool publish = true)
    {
        if (_finished) return;
        if (publish && _publishCommand is not null) PublishCancel();
        _finished = true;
        UnqueueRevealUpdate();
        ReleaseSelectionInput();
        if (!OverlayVisualFactory.AnimationsEnabled())
        {
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            return;
        }

        // The frozen frame cross-fades into the live desktop; only the cancel path animates,
        // the mouse-up path shows the selection rectangle and closes without a window fade.
        var fade = Fade(1, 0);
        switch (_exitFade)
        {
            case OverlayExitFade.Root:
                fade.Completed += (_, _) => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                _visual.Root.BeginAnimation(OpacityProperty, fade);
                break;
            case OverlayExitFade.DimLayers:
                // No window transparency: the overlay layers melt away while the frozen
                // frame (identical to the live desktop) stays until close.
                var completed = new DoubleAnimation(1, 0, ExitFadeDuration) { EasingFunction = EaseOut() };
                completed.Completed += (_, _) => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                _visual.Dim.BeginAnimation(OpacityProperty, completed);
                _visual.Sheen.BeginAnimation(OpacityProperty, Fade(1, 0));
                _visual.Halo.BeginAnimation(OpacityProperty, Fade(1, 0));
                _visual.Accent.BeginAnimation(OpacityProperty, Fade(1, 0));
                OverlayVisualFactory.BeginChipExit(_visual);
                break;
            default:
                fade.Completed += (_, _) => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                BeginAnimation(OpacityProperty, fade);
                break;
        }

        if (_clickThroughOnCancel) MakeClickThrough();
        IsHitTestVisible = false;
    }

    private DoubleAnimation Fade(double from, double to) =>
        new(from, to, ExitFadeDuration) { EasingFunction = EaseOut() };

    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GwlExStyle);
        // Transparent only: setting WS_EX_LAYERED by hand detaches WPF's DWM redirection
        // surface and the window renders black; WPF enables layering itself for Opacity < 1.
        NativeMethods.SetWindowLongW(hwnd, NativeMethods.GwlExStyle, style | NativeMethods.WsExTransparent);
    }

    private void FinishShutdown()
    {
        _finished = true;
        UnqueueRevealUpdate();
        DisposeVisualResources();
        Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }

    private void PublishCancel()
    {
        if (_cancelPublished) return;
        _cancelPublished = true;
        _publishCommand?.Invoke(new CancelSession());
    }

    private void DisposeVisualResources()
    {
        UnqueueEntranceRipple();
        _visual.Waveform.Dispose();
        _visual.SceneRipples.Dispose();
        foreach (var ripple in _controlRipples) ripple.Dispose();
        _controlRipples.Clear();
        UnqueueRevealUpdate();
    }
}
