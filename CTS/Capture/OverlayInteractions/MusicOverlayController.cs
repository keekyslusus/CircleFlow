using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class MusicOverlayController : IDisposable
{
    private readonly MusicOverlayVisual _visual;
    private readonly BottomOverlayLayoutTransitions _layoutTransitions;
    private readonly OverlayEffectsVisual _effects;
    private readonly FrameworkElement _root;
    private readonly UiStrings _strings;
    private readonly bool _lightTheme;
    private readonly Func<OverlayInteractionMode> _getMode;
    private readonly Action _startRequested;
    private readonly Action _cancelRequested;
    private readonly Action<IOverlayCommand> _resultCommandRequested;
    private readonly ClipboardCopyService _clipboardCopy;
    private readonly Func<bool> _animationsEnabled;
    private readonly List<DispatcherTimer> _copyTimers = [];
    private readonly List<IDisposable> _resultRipples = [];
    private DispatcherOperation? _matchRippleOperation;
    private FrameworkElement? _currentResultCard;
    private CardTransitions.ExitHandle? _pendingResultExit;
    private long _resultGeneration;
    private bool _disposed;

    internal int PendingCopyRestoreCount => _copyTimers.Count;

    internal bool HasPendingMatchRipple =>
        _matchRippleOperation?.Status == DispatcherOperationStatus.Pending;

    internal bool HasPendingResultExit => _pendingResultExit is not null;

    internal void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        _visual.Button.IsEnabled = enabled;
    }

    internal MusicOverlayController(
        MusicOverlayVisual visual,
        BottomOverlayLayoutTransitions layoutTransitions,
        OverlayEffectsVisual effects,
        FrameworkElement root,
        UiStrings strings,
        bool lightTheme,
        Func<OverlayInteractionMode> getMode,
        Action startRequested,
        Action cancelRequested,
        Action<IOverlayCommand> resultCommandRequested,
        ClipboardCopyService clipboardCopy,
        Func<bool> animationsEnabled)
    {
        _visual = visual;
        _layoutTransitions = layoutTransitions;
        _effects = effects;
        _root = root;
        _strings = strings;
        _lightTheme = lightTheme;
        _getMode = getMode;
        _startRequested = startRequested;
        _cancelRequested = cancelRequested;
        _resultCommandRequested = resultCommandRequested;
        _clipboardCopy = clipboardCopy ?? throw new ArgumentNullException(nameof(clipboardCopy));
        _animationsEnabled = animationsEnabled;

        _visual.Button.Click += OnMusicButtonClick;
    }

    internal void ShowListening()
    {
        if (_disposed) return;
        BeginResultExit();
        MusicOverlayVisualPresenter.SetListeningState(_visual, listening: true, _lightTheme);
        _visual.Waveform.Start();
        _visual.Button.ToolTip = _strings.CancelMusicRecognition;
        AutomationProperties.SetName(_visual.Button, _strings.CancelMusicRecognition);
    }

    internal void ShowResult(MusicRecognitionOutcome outcome)
    {
        if (_disposed || outcome.Status == MusicRecognitionStatus.Canceled) return;
        CancelPendingResultExit();
        AbortPendingMatchRipple();
        DisposeResultRipples();
        _resultGeneration++;
        var animationsEnabled = _animationsEnabled();
        _visual.Waveform.Stop();
        MusicOverlayVisualPresenter.SetListeningState(_visual, listening: false, _lightTheme);
        var card = _layoutTransitions.Apply(() =>
        {
            _currentResultCard = null;
            _visual.ResultHost.Children.Clear();
            ResetResultHost();
            var presented = MusicOverlayVisualPresenter.PresentResult(
                _visual,
                outcome,
                _strings,
                _lightTheme,
                _resultCommandRequested,
                CopyTrackInfo);
            _currentResultCard = presented;
            return presented;
        }, animationsEnabled);
        StateCardTransitions.BeginEntrance(card, animationsEnabled);
        _resultRipples.AddRange(OverlayVisualResources.AttachControlRipples(_visual.ResultHost));
        if (outcome.Status != MusicRecognitionStatus.Matched || !animationsEnabled) return;

        var generation = _resultGeneration;
        _matchRippleOperation = _root.Dispatcher.BeginInvoke(() =>
        {
            _matchRippleOperation = null;
            if (_disposed || _getMode() != OverlayInteractionMode.MusicResult ||
                generation != _resultGeneration || !ReferenceEquals(_currentResultCard, card)) return;
            _visual.ResultHost.UpdateLayout();
            var origin = _visual.ResultHost.TransformToAncestor(_root).Transform(
                new Point(
                    _visual.ResultHost.ActualWidth / 2,
                    _visual.ResultHost.ActualHeight / 2));
            _effects.SceneRipples.Emit(new SceneRippleRequest(origin, SceneRipplePreset.MusicMatch, 1));
        }, DispatcherPriority.Loaded);
    }

    internal void DismissResult()
    {
        if (_disposed) return;
        BeginResultExit();
        _visual.Button.ToolTip = _strings.MusicRecognitionAction;
        AutomationProperties.SetName(_visual.Button, _strings.MusicRecognitionAction);
    }

    internal void ReportAudio(MusicVisualizationFrame frame)
    {
        if (_disposed || _getMode() != OverlayInteractionMode.Listening) return;
        _visual.Waveform.Report(frame);
        if (!frame.IsTransient || !_animationsEnabled()) return;
        var center = _visual.Waveform.TransformToAncestor(_root).Transform(
            new Point(_visual.Waveform.ActualWidth / 2, _visual.Waveform.ActualHeight / 2));
        _effects.SceneRipples.Emit(new SceneRippleRequest(
            center,
            SceneRipplePreset.AudioTransient,
            frame.NormalizedPeak));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visual.Button.Click -= OnMusicButtonClick;
        foreach (var timer in _copyTimers)
        {
            timer.Stop();
            timer.Tick -= OnRestoreCopy;
        }
        _copyTimers.Clear();
        AbortPendingMatchRipple();
        CancelPendingResultExit();
        DisposeResultRipples();
        _layoutTransitions.Settle();
        ClearResultVisual();
        _visual.LoadingIndicator.Dispose();
        _visual.Waveform.Dispose();
    }

    private void OnMusicButtonClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing) return;
        if (_getMode() is OverlayInteractionMode.Selecting or OverlayInteractionMode.TraceResult) _startRequested();
        else _cancelRequested();
        e.Handled = true;
    }

    private void CopyTrackInfo(string text, Button button)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing) return;
        if (_clipboardCopy.TryCopy(text))
        {
            MusicOverlayVisualPresenter.SetCopyConfirmed(button, confirmed: true, _strings, _lightTheme);
            var restore = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            restore.Tick += OnRestoreCopy;
            restore.Tag = button;
            _copyTimers.Add(restore);
            restore.Start();
        }
        else
        {
            MusicOverlayVisualPresenter.SetCopyConfirmed(button, confirmed: false, _strings, _lightTheme);
        }
    }

    private void OnRestoreCopy(object? sender, EventArgs e)
    {
        if (sender is not DispatcherTimer timer) return;
        timer.Stop();
        timer.Tick -= OnRestoreCopy;
        _copyTimers.Remove(timer);
        if (_disposed || timer.Tag is not Button button) return;
        MusicOverlayVisualPresenter.SetCopyConfirmed(button, confirmed: false, _strings, _lightTheme);
    }

    private void DisposeResultRipples()
    {
        foreach (var ripple in _resultRipples) ripple.Dispose();
        _resultRipples.Clear();
    }

    private void BeginResultExit()
    {
        DisposeResultRipples();
        AbortPendingMatchRipple();
        if (_pendingResultExit is not null) return;

        _visual.ResultHost.IsHitTestVisible = false;
        var card = _currentResultCard;
        if (card is null || !_visual.ResultHost.Children.Contains(card))
        {
            ClearResultVisual();
            return;
        }

        var generation = ++_resultGeneration;
        var animationsEnabled = _animationsEnabled();
        var exit = StateCardTransitions.BeginExit(
            card,
            animationsEnabled,
            () => CompleteResultExit(card, generation, animationsEnabled));
        if (!exit.IsCompleted && ReferenceEquals(_currentResultCard, card) &&
            generation == _resultGeneration)
            _pendingResultExit = exit;
        else
            exit.Dispose();
    }

    private void CompleteResultExit(FrameworkElement card, long generation, bool animationsEnabled)
    {
        if (_disposed || generation != _resultGeneration ||
            !ReferenceEquals(_currentResultCard, card)) return;
        var completedExit = _pendingResultExit;
        _pendingResultExit = null;
        completedExit?.Dispose();
        _layoutTransitions.Apply(ClearResultVisual, animationsEnabled);
    }

    private void CancelPendingResultExit()
    {
        _pendingResultExit?.Dispose();
        _pendingResultExit = null;
    }

    private void AbortPendingMatchRipple()
    {
        if (_matchRippleOperation?.Status == DispatcherOperationStatus.Pending)
            _matchRippleOperation.Abort();
        _matchRippleOperation = null;
    }

    private void ClearResultVisual()
    {
        _currentResultCard = null;
        _visual.ResultHost.Children.Clear();
        _visual.ResultHost.Visibility = Visibility.Collapsed;
        ResetResultHost();
    }

    private void ResetResultHost()
    {
        _visual.ResultHost.BeginAnimation(UIElement.OpacityProperty, null);
        _visual.ResultHost.Opacity = 1;
        _visual.ResultHost.IsHitTestVisible = true;
    }
}
