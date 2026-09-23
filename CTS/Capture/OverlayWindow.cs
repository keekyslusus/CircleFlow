using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Interop;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;
using CircleToSearch.Translation;
using GdiBitmap = System.Drawing.Bitmap;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;

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

public sealed class OverlayWindow : Window
{
    private const double ChipEdgeMarginDips = 32;
    private static readonly TimeSpan ForegroundCheckInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ExitFadeDuration = TimeSpan.FromMilliseconds(160);
    private const string InputLanguageSwitcherWindowClass = "Shell_InputSwitchTopLevelWindow";

    private readonly GdiBitmap _frame;
    private readonly BitmapSource _capturedImage;
    private readonly OverlayVisual _visual;
    private readonly OverlayExitFade _exitFade;
    private readonly bool _clickThroughOnCancel;
    private readonly Point? _entranceOrigin;
    private readonly Action<IOverlayCommand>? _publishCommand;
    private readonly UiStrings _strings;
    private readonly OverlayInteractionState _interaction = new();
    private readonly OverlayControllers _controllers;
    private readonly SelectionOverlayController _selection;
    private readonly ImageSelectionOverlayController _imageSelection;
    private readonly TextSelectionOverlayController _textSelection;
    private readonly PointerGestureRouter _pointer;
    private readonly OcrOverlayController _ocr;
    private readonly OverlayImageTextCoordinator _imageText;
    private readonly KeyboardInputLanguageSource _inputLanguage;
    private readonly KeyboardLanguageSnapshot _initialInputLanguage;
    private readonly ScreenTranslationOverlayController _translation;
    private readonly ProviderMenuController _provider;
    private readonly MusicOverlayController _music;
    private readonly TraceOverlayController _trace;
    private readonly ActionTrayOverlayController _actionTray;
    private readonly ToastOverlayController _toast;
    private readonly DebugOverlayController _debug;
    private bool _cancelPublished;
    private bool _exitFadeStarted;
    private bool _entranceRipplePending;
    private bool _resourcesDisposed;
    private DispatcherTimer? _inputSwitcherCheck;

    internal OverlayInteractionMode Mode => _interaction.Mode;

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
        IOverlayControllerFactory controllerFactory,
        bool allowsTransparency = true,
        OverlayExitFade exitFade = OverlayExitFade.Root,
        bool clickThroughOnCancel = true,
        bool overscan = true,
        GdiPoint? entranceOrigin = null,
        IReadOnlyList<SearchProviderDescriptor>? providers = null,
        string? initialProviderId = null,
        Action<IOverlayCommand>? publishCommand = null,
        string? ocrLanguageTag = null,
        string translationTargetLanguageTag = "en",
        KeyboardLanguageSnapshot inputLanguage = default)
    {
        _frame = frame;
        _exitFade = exitFade;
        _clickThroughOnCancel = clickThroughOnCancel;
        _publishCommand = publishCommand;
        _strings = strings;
        _initialInputLanguage = inputLanguage;
        ArgumentNullException.ThrowIfNull(controllerFactory);
        _entranceOrigin = entranceOrigin is null
            ? null
            : new Point(
                (entranceOrigin.Value.X - monitor.Left) / scale + (overscan ? 1 : 0),
                (entranceOrigin.Value.Y - monitor.Top) / scale + (overscan ? 1 : 0));
        var availableProviders = providers ?? [];
        var selectedProviderId = initialProviderId ?? string.Empty;

        ConfigureWindow(monitor, scale, strings, allowsTransparency, overscan);
        var frameSource = CreateFrozenFrame(frame);
        _capturedImage = frameSource;
        var visualSize = new Size(Width, Height);
        var bottomMargin = ChipBottomMargin(monitor, workArea, scale) + (overscan ? 1 : 0);
        _visual = availableProviders.Count == 0
            ? OverlayVisualFactory.CreateRoot(frameSource, visualSize, bottomMargin, strings)
            : OverlayVisualFactory.CreateRoot(
                frameSource,
                visualSize,
                bottomMargin,
                SystemTheme.IsLight(),
                strings,
                availableProviders,
                selectedProviderId);
        if (overscan) _visual.Selection.Screenshot.Margin = new Thickness(1);
        Content = new AdornerDecorator { Child = _visual.Root };

        try
        {
            _controllers = controllerFactory.Create(new OverlayControllerContext(
                _visual,
                this,
                monitor,
                scale,
                options,
                overscan,
                availableProviders,
                selectedProviderId,
                strings,
                _publishCommand,
                CreateSelectionCopy,
                () => _interaction.CanAcceptSelectionInput,
                CanAcceptPointerInput,
                CanStartSelection,
                OnSelectionStarted,
                OnSelectionCompleted,
                OnSelectionRejected,
                OnSelectionHoldCompleted,
                () => !_interaction.IsFinished,
                providerId => _publishCommand?.Invoke(new ProviderSelected(providerId)),
                () => Mode,
                StartMusicRecognition,
                CancelMusicRecognition,
                scenario => _publishCommand?.Invoke(new MusicDebugScenarioSelected(scenario)),
                HandleMusicResultCommand,
                ApplyModeTransition,
                ocrLanguageTag,
                translationTargetLanguageTag,
                inputLanguage,
                () => CancelInternal(),
                OnSelectionDrawn));
        }
        catch
        {
            DisposeUnownedVisualResources();
            Content = null;
            throw;
        }
        _selection = _controllers.Selection;
        _imageSelection = _controllers.ImageSelection;
        _textSelection = _controllers.TextSelection;
        _pointer = _controllers.Pointer;
        _ocr = _controllers.Ocr;
        _imageText = _controllers.ImageText;
        _inputLanguage = _controllers.InputLanguage;
        _translation = _controllers.Translation;
        _provider = _controllers.Provider;
        _music = _controllers.Music;
        _trace = _controllers.Trace;
        _actionTray = _controllers.ActionTray;
        _toast = _controllers.Toast;
        _debug = _controllers.Debug;

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        Deactivated += OnDeactivated;
        Activated += OnActivated;
        Closed += OnClosed;
        Dispatcher.ShutdownStarted += OnDispatcherShutdownStarted;
    }

    internal OverlayWindow(
        GdiBitmap frame,
        GdiRectangle monitor,
        GdiRectangle workArea,
        double scale,
        OverlayLaunchOptions options,
        Action<IOverlayCommand> publishCommand,
        IOverlayControllerFactory controllerFactory,
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
            controllerFactory,
            allowsTransparency,
            exitFade,
            clickThroughOnCancel,
            overscan,
            entranceOrigin,
            options.Providers,
            options.InitialProviderId,
            publishCommand,
            options.SessionOptions.OcrLanguageTag,
            options.SessionOptions.TranslationTargetLanguageTag,
            options.SessionOptions.InputLanguage)
    {
    }

    public void CancelFromCoordinator() => Dispatcher.BeginInvoke(new Action(() => CancelInternal()));

    internal void SetDebugPanelOpen(bool open)
    {
        _debug.SetOpen(open);
        if (open) _provider.SetOpen(false);
        _imageText.SetAvailable(!open && Mode is (OverlayInteractionMode.Selecting or OverlayInteractionMode.TranslationShown));
    }

    internal bool IsOverlayChromeInteraction(object? originalSource, Point windowPoint)
    {
        var hit = InputHitTest(windowPoint) as DependencyObject;
        return IsWithin(originalSource as DependencyObject, _visual.Bottom.Root) ||
               IsWithin(originalSource as DependencyObject, _visual.Debug.Panel) ||
               IsWithin(originalSource as DependencyObject, _visual.TextSelection.Toolbar.Surface) ||
               IsWithin(originalSource as DependencyObject, _visual.ImageSelection.Toolbar.Surface) ||
               IsWithin(hit, _visual.Bottom.Root) ||
               IsWithin(hit, _visual.Debug.Panel) ||
               IsWithin(hit, _visual.TextSelection.Toolbar.Surface) ||
               IsWithin(hit, _visual.ImageSelection.Toolbar.Surface) ||
               _visual.Bottom.Root.IsMouseOver ||
               _visual.Debug.Panel.IsMouseOver ||
               _visual.TextSelection.Toolbar.Surface.IsMouseOver ||
               _visual.ImageSelection.Toolbar.Surface.IsMouseOver;
    }

    internal void ShowListening()
    {
        if (_interaction.IsFinished || Mode == OverlayInteractionMode.Listening) return;
        if (Mode != OverlayInteractionMode.Selecting && Mode != OverlayInteractionMode.MusicResult) return;
        ApplyModeTransition(OverlayInteractionMode.Listening);
    }

    internal void ReportAudio(MusicVisualizationFrame frame) => _music.ReportAudio(frame);

    internal void ShowTraceResult(VisualSearchPreparationOutcome outcome)
        => _trace.ShowResult(outcome);

    internal void ShowMusicResult(MusicRecognitionOutcome outcome)
    {
        if (_interaction.IsFinished || Mode != OverlayInteractionMode.Listening ||
            outcome.Status == MusicRecognitionStatus.Canceled) return;
        ApplyModeTransition(OverlayInteractionMode.MusicResult);
        _music.ShowResult(outcome);
    }

    internal bool IsActionTrayInteraction(object? originalSource, Point windowPoint) =>
        IsOverlayChromeInteraction(originalSource, windowPoint);

    internal void ShowTranslation(ScreenTranslationResult result) => _translation.ShowResult(result);

    internal void ShowTranslationFailure(Guid requestId, TranslationFailure failure) =>
        _translation.ShowFailure(requestId, failure);

    internal void CloseFromSession()
    {
        if (_exitFadeStarted || Dispatcher.HasShutdownStarted) return;
        if (_interaction.IsFinished) FinishShutdown();
        else CancelInternal(publish: false);
    }

    private void ConfigureWindow(
        GdiRectangle monitor,
        double scale,
        UiStrings strings,
        bool allowsTransparency,
        bool overscan)
    {
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
        Background = CreateFrozenSolidBrush(
            allowsTransparency ? PluginPalette.Transparent : PluginPalette.OpaqueBlack);
        if (allowsTransparency) AllowsTransparency = true;
        if (!overscan) return;
        Left -= 1;
        Top -= 1;
        Width += 2;
        Height += 2;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Keyboard.Focus(this);
        _actionTray.ShowEntrance();
        QueueEntranceRipple();
        _inputLanguage.Attach(this, _initialInputLanguage);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_publishCommand is not null &&
            e.Key == Key.D &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            SetDebugPanelOpen(!_debug.IsOpen);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape) return;
        if (_debug.IsOpen)
        {
            SetDebugPanelOpen(false);
        }
        else if (_provider.IsOpen)
        {
            _provider.SetOpen(false);
        }
        else if (_imageSelection.CloseAskPrompt())
        {
            Keyboard.Focus(this);
        }
        else if (_translation.HandleEscape())
        {
        }
        else if (_textSelection.IsActionMenuOpen)
        {
            _textSelection.Dismiss();
            Cursor = Cursors.Cross;
        }
        else
        {
            CancelInternal();
        }
        e.Handled = true;
    }

    private bool CanStartSelection(object? originalSource, Point point)
    {
        var actionInteraction = IsOverlayChromeInteraction(originalSource, point);
        if (_provider.IsOpen && !actionInteraction)
        {
            _provider.SetOpen(false);
            return false;
        }
        if (_textSelection.HasSelection && !actionInteraction) _textSelection.Dismiss();
        return CanAcceptPointerInput() && !actionInteraction;
    }

    private bool CanAcceptPointerInput() =>
        _imageSelection?.IsCompleting != true &&
        (_interaction.CanAcceptSelectionInput ||
        (_translation.IsImageShown && Mode == OverlayInteractionMode.TranslationShown));

    private void OnSelectionStarted()
    {
        _imageSelection.Dismiss();
        _textSelection.Dismiss();
        if (Mode == OverlayInteractionMode.TranslationShown)
        {
            _translation.CommitVisibleImage();
            ApplyModeTransition(OverlayInteractionMode.Selecting);
        }
        _actionTray.HideForSelection();
    }

    private void OnSelectionDrawn()
    {
        if (!_pointer.IsActionSelection)
            _publishCommand?.Invoke(new VisualSelectionStarted(_provider.SelectedProviderId));
    }

    private void OnSelectionCompleted(GdiRectangle bounds)
    {
        if (_interaction.IsFinished) return;
        if (_pointer.IsActionSelection && _pointer.ActiveGesture == ActivePointerGesture.Lasso)
        {
            _imageSelection.Select(bounds);
            return;
        }
        if (_trace.TryStart(_provider.SelectedProviderId, bounds)) return;
        var waitForSelectionHold = _pointer.ActiveGesture == ActivePointerGesture.Lasso;
        var usesCapturedImage = ReferenceEquals(_visual.Selection.Screenshot.Source, _capturedImage);
        var selection = usesCapturedImage ? new SelectionOutcome(bounds, _frame) : CreateSelectionCopy(bounds);
        FrameTransferred = usesCapturedImage;
        ApplyModeTransition(OverlayInteractionMode.Closing);
        if (_publishCommand is null) Outcome = OverlayOutcome.VisualSelection(selection);
        else
        {
            try { _publishCommand(new VisualSelection(selection, _provider.SelectedProviderId)); }
            catch
            {
                selection.Dispose();
                throw;
            }
        }
        if (!waitForSelectionHold) FinishShutdown();
    }

    private void OnSelectionHoldCompleted()
    {
        if (Mode is OverlayInteractionMode.TraceLoading or OverlayInteractionMode.TraceResult)
            _selection.FadeSelectionVisuals();
        else if (Mode == OverlayInteractionMode.Closing)
            FinishShutdown();
    }

    private void OnSelectionRejected()
    {
        if (_interaction.IsFinished || Mode != OverlayInteractionMode.Selecting) return;
        _actionTray.Restore();
        _toast.Show(new ToastNotification(_strings.SelectionTooSmall, ToastTone.Error));
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        var windowsShortcut = Keyboard.Modifiers.HasFlag(ModifierKeys.Windows);
        Dispatcher.BeginInvoke(() =>
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero || IsInputLanguageSwitcherWindow(foreground))
            {
                StartInputSwitcherCheck();
                return;
            }
            CancelIfStillInactive(honorChrome: !windowsShortcut);
        }, DispatcherPriority.ContextIdle);
    }

    private void OnActivated(object? sender, EventArgs e) => StopInputSwitcherCheck();

    private void StartInputSwitcherCheck()
    {
        if (_inputSwitcherCheck is not null || _resourcesDisposed) return;
        _inputSwitcherCheck = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = ForegroundCheckInterval,
        };
        _inputSwitcherCheck.Tick += OnInputSwitcherCheck;
        _inputSwitcherCheck.Start();
    }

    private void OnInputSwitcherCheck(object? sender, EventArgs e)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsInputLanguageSwitcherWindow(foreground)) return;
        StopInputSwitcherCheck();
        if (foreground == new WindowInteropHelper(this).Handle) return;
        if (!IsActive) CancelIfStillInactive(honorChrome: false);
    }

    private void StopInputSwitcherCheck()
    {
        if (_inputSwitcherCheck is null) return;
        _inputSwitcherCheck.Stop();
        _inputSwitcherCheck.Tick -= OnInputSwitcherCheck;
        _inputSwitcherCheck = null;
    }

    private static bool IsInputLanguageSwitcherWindow(IntPtr window)
    {
        var className = new System.Text.StringBuilder(64);
        return NativeMethods.GetClassNameW(window, className, className.Capacity) > 0 &&
               string.Equals(className.ToString(), InputLanguageSwitcherWindowClass,
                   StringComparison.Ordinal);
    }

    private void CancelIfStillInactive(bool honorChrome = true)
    {
        if (_interaction.IsFinished || IsActive) return;
        if (honorChrome && IsOverlayChromeInteraction(null, Mouse.GetPosition(this))) return;
        CancelInternal();
    }

    private void StartMusicRecognition()
    {
        if (Mode == OverlayInteractionMode.TraceResult)
            ApplyModeTransition(OverlayInteractionMode.Selecting);
        SetDebugPanelOpen(false);
        if (_publishCommand is null)
        {
            ApplyModeTransition(OverlayInteractionMode.Closing);
            Outcome = OverlayOutcome.MusicRecognition();
            IsHitTestVisible = false;
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            return;
        }
        ApplyModeTransition(OverlayInteractionMode.Listening);
        _publishCommand(new StartMusicRecognition());
    }

    private void CancelMusicRecognition()
    {
        PublishCancel();
        CancelInternal(publish: false);
    }

    private void HandleMusicResultCommand(IOverlayCommand command)
    {
        if (_interaction.IsFinished) return;
        if (command is DismissMusicResult && Mode == OverlayInteractionMode.MusicResult)
            ApplyModeTransition(OverlayInteractionMode.Selecting);
        else if (command is RetryMusicRecognition && Mode == OverlayInteractionMode.MusicResult)
            ApplyModeTransition(OverlayInteractionMode.Listening);
        _publishCommand?.Invoke(command);
    }

    private void ApplyModeTransition(OverlayInteractionMode target)
    {
        if (!_interaction.TransitionTo(target)) return;
        if (target is not OverlayInteractionMode.Selecting and not OverlayInteractionMode.TranslationShown)
            _imageText.SetAvailable(false);
        switch (target)
        {
            case OverlayInteractionMode.TraceLoading:
            case OverlayInteractionMode.TraceResult:
                _imageSelection.Dismiss();
                _pointer.Cancel();
                _textSelection.Dismiss();
                _provider.SetOpen(false);
                _debug.SetOpen(false);
                _visual.Bottom.Root.Visibility = Visibility.Visible;
                SetConflictingControlsEnabled(target == OverlayInteractionMode.TraceResult);
                _translation.SetActionEnabled(false);
                if (target == OverlayInteractionMode.TraceLoading)
                    _actionTray.Restore();
                _selection.FadeSelectionVisuals();
                Cursor = Cursors.Arrow;
                break;
            case OverlayInteractionMode.Listening:
                _imageSelection.Dismiss();
                _pointer.Cancel();
                _textSelection.Dismiss();
                _translation.SetActionEnabled(false);
                _music.ShowListening();
                Cursor = Cursors.Arrow;
                _selection.FadeSelectionVisuals();
                break;
            case OverlayInteractionMode.MusicResult:
                _pointer.Cancel();
                _translation.SetActionEnabled(false);
                Cursor = Cursors.Arrow;
                break;
            case OverlayInteractionMode.Selecting:
                _trace.DismissResult();
                _music.DismissResult();
                _translation.DismissStateCard();
                _selection.RestoreSelectionVisuals();
                SetConflictingControlsEnabled(true);
                Cursor = Cursors.Cross;
                break;
            case OverlayInteractionMode.TranslationConsent:
            case OverlayInteractionMode.TranslationResult:
            case OverlayInteractionMode.Translating:
            case OverlayInteractionMode.TranslationShown:
                _pointer.Cancel();
                _textSelection.Dismiss();
                _provider.SetOpen(false);
                _debug.SetOpen(false);
                SetConflictingControlsEnabled(false);
                _translation.SetActionEnabled(target is OverlayInteractionMode.Translating or OverlayInteractionMode.TranslationShown);
                Cursor = Cursors.Arrow;
                break;
            case OverlayInteractionMode.Closing:
                _imageSelection.Dismiss();
                _pointer.Cancel();
                _textSelection.Dismiss();
                _translation.CancelForClosing();
                _provider.SetOpen(false);
                _debug.SetOpen(false);
                _toast.SettleForClosing();
                _visual.Bottom.LayoutTransitions.Settle();
                _translation.SetActionEnabled(false);
                break;
        }
        if (target is OverlayInteractionMode.Selecting or OverlayInteractionMode.TranslationShown)
            _imageText.SetAvailable(!_debug.IsOpen);
        if (target == OverlayInteractionMode.Closing) _inputLanguage.Dispose();
        _imageSelection.Refresh(target);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_imageSelection.Bounds is not null && !IsOverlayChromeInteraction(e.OriginalSource, e.GetPosition(this)))
        {
            if (!CanAcceptPointerInput()) { e.Handled = true; return; }
            _imageSelection.Dismiss();
            _selection.Cancel();
            if (Mode == OverlayInteractionMode.TranslationShown)
                ApplyModeTransition(OverlayInteractionMode.Selecting);
            _actionTray.Restore();
        }
        if (!_textSelection.HasSelection) return;
        if (IsWithin(e.OriginalSource as DependencyObject, _visual.TextSelection.Toolbar.Surface)) return;
        _textSelection.Dismiss();
    }

    private void SetConflictingControlsEnabled(bool enabled)
    {
        _provider.SetEnabled(enabled);
        _music.SetEnabled(enabled);
        _translation.SetActionEnabled(true);
    }

    private void CancelInternal(bool publish = true)
    {
        if (_interaction.IsFinished) return;
        _exitFadeStarted = true;
        ApplyModeTransition(OverlayInteractionMode.Closing);
        if (publish && _publishCommand is not null) PublishCancel();
        if (!IsVisible || !OverlayVisualResources.AnimationsEnabled())
        {
            FinishShutdown();
            return;
        }

        var fade = Fade(1, 0);
        switch (_exitFade)
        {
            case OverlayExitFade.Root:
                fade.Completed += (_, _) => FinishShutdown();
                _visual.Root.BeginAnimation(OpacityProperty, fade);
                break;
            case OverlayExitFade.DimLayers:
                var completed = Fade(1, 0);
                completed.Completed += (_, _) => FinishShutdown();
                _visual.Selection.Dim.BeginAnimation(OpacityProperty, completed);
                _visual.Selection.Sheen.BeginAnimation(OpacityProperty, Fade(1, 0));
                _visual.Selection.Halo.BeginAnimation(OpacityProperty, Fade(1, 0));
                _visual.Selection.Accent.BeginAnimation(OpacityProperty, Fade(1, 0));
                _actionTray.BeginExit();
                break;
            default:
                fade.Completed += (_, _) => FinishShutdown();
                BeginAnimation(OpacityProperty, fade);
                break;
        }

        if (_clickThroughOnCancel) MakeClickThrough();
        IsHitTestVisible = false;
    }

    private void QueueEntranceRipple()
    {
        if (_entranceOrigin is null || _entranceRipplePending) return;
        _entranceRipplePending = true;
        CompositionTarget.Rendering += EmitEntranceRippleOnFirstFrame;
    }

    private void EmitEntranceRippleOnFirstFrame(object? sender, EventArgs e)
    {
        if (_interaction.IsFinished)
        {
            UnqueueEntranceRipple();
            return;
        }
        if (_visual.Effects.SceneRippleLayer.ActualWidth <= 0 ||
            _visual.Effects.SceneRippleLayer.ActualHeight <= 0) return;
        UnqueueEntranceRipple();
        _visual.Effects.SceneRipples.Emit(new SceneRippleRequest(
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

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongW(hwnd, NativeMethods.GwlExStyle, style | NativeMethods.WsExTransparent);
    }

    private void FinishShutdown()
    {
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
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;
        Loaded -= OnLoaded;
        PreviewKeyDown -= OnPreviewKeyDown;
        PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        Deactivated -= OnDeactivated;
        Activated -= OnActivated;
        StopInputSwitcherCheck();
        Closed -= OnClosed;
        Dispatcher.ShutdownStarted -= OnDispatcherShutdownStarted;
        UnqueueEntranceRipple();
        _controllers.Dispose();
        _visual.Bottom.LayoutTransitions.Dispose();
        _visual.Effects.SceneRipples.Dispose();
    }

    private void OnClosed(object? sender, EventArgs e) => DisposeVisualResources();

    private void OnDispatcherShutdownStarted(object? sender, EventArgs e) => DisposeVisualResources();

    private DoubleAnimation Fade(double from, double to) =>
        new(from, to, ExitFadeDuration) { EasingFunction = EaseOut() };

    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };

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

    private SelectionOutcome CreateSelectionCopy(GdiRectangle bounds) =>
        ReferenceEquals(_visual.Selection.Screenshot.Source, _capturedImage)
            ? new(bounds, (GdiBitmap)_frame.Clone())
            : VisibleImage.CreateSelection((BitmapSource)_visual.Selection.Screenshot.Source, bounds);

    private void DisposeUnownedVisualResources()
    {
        _visual.TranslationAction.LoadingIndicator.Dispose();
        _visual.Music.LoadingIndicator.Dispose();
        _visual.Music.Waveform.Dispose();
        _visual.Bottom.LayoutTransitions.Dispose();
        _visual.Effects.SceneRipples.Dispose();
    }

    private static SolidColorBrush CreateFrozenSolidBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static double ChipBottomMargin(GdiRectangle monitor, GdiRectangle workArea, double scale) =>
        (monitor.Bottom - workArea.Bottom) / scale + ChipEdgeMarginDips;
}
