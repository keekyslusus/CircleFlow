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
    private readonly OverlayEffectsVisual _effects;
    private readonly FrameworkElement _root;
    private readonly UiStrings _strings;
    private readonly bool _lightTheme;
    private readonly bool _debugEnabled;
    private readonly Func<OverlayInteractionMode> _getMode;
    private readonly Action _startRequested;
    private readonly Action _cancelRequested;
    private readonly Action<MusicDebugScenario> _debugScenarioSelected;
    private readonly Action<IOverlayCommand> _resultCommandRequested;
    private readonly Action<string> _setClipboard;
    private readonly Func<bool> _animationsEnabled;
    private readonly List<Button> _debugButtons = [];
    private readonly List<DispatcherTimer> _copyTimers = [];
    private readonly List<IDisposable> _resultRipples = [];
    private DispatcherOperation? _matchRippleOperation;
    private bool _disposed;

    internal int PendingCopyRestoreCount => _copyTimers.Count;

    internal bool HasPendingMatchRipple =>
        _matchRippleOperation?.Status == DispatcherOperationStatus.Pending;

    internal MusicOverlayController(
        MusicOverlayVisual visual,
        OverlayEffectsVisual effects,
        FrameworkElement root,
        UiStrings strings,
        bool lightTheme,
        bool debugEnabled,
        Func<OverlayInteractionMode> getMode,
        Action startRequested,
        Action cancelRequested,
        Action<MusicDebugScenario> debugScenarioSelected,
        Action<IOverlayCommand> resultCommandRequested,
        Action<string> setClipboard,
        Func<bool> animationsEnabled)
    {
        _visual = visual;
        _effects = effects;
        _root = root;
        _strings = strings;
        _lightTheme = lightTheme;
        _debugEnabled = debugEnabled;
        _getMode = getMode;
        _startRequested = startRequested;
        _cancelRequested = cancelRequested;
        _debugScenarioSelected = debugScenarioSelected;
        _resultCommandRequested = resultCommandRequested;
        _setClipboard = setClipboard;
        _animationsEnabled = animationsEnabled;

        _visual.Button.Click += OnMusicButtonClick;
        foreach (var button in _visual.DebugScenarioButtons.Children.OfType<Button>())
        {
            button.Click += OnDebugScenarioClick;
            _debugButtons.Add(button);
        }
    }

    internal bool IsDebugPanelOpen { get; private set; }

    internal void SetDebugPanelOpen(bool open)
    {
        if (_disposed || !_debugEnabled || (open && _getMode() == OverlayInteractionMode.Closing)) return;
        IsDebugPanelOpen = open;
        _visual.DebugPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void ShowListening()
    {
        if (_disposed) return;
        _visual.ResultHost.Visibility = Visibility.Collapsed;
        _visual.ResultHost.Children.Clear();
        DisposeResultRipples();
        MusicOverlayVisualPresenter.SetListeningState(_visual, listening: true, _lightTheme);
        _visual.Waveform.Start();
        _visual.Button.ToolTip = _strings.CancelMusicRecognition;
        AutomationProperties.SetName(_visual.Button, _strings.CancelMusicRecognition);
    }

    internal void ShowResult(MusicRecognitionOutcome outcome)
    {
        if (_disposed || outcome.Status == MusicRecognitionStatus.Canceled) return;
        _visual.Waveform.Stop();
        MusicOverlayVisualPresenter.SetListeningState(_visual, listening: false, _lightTheme);
        var card = MusicOverlayVisualPresenter.PresentResult(
            _visual,
            outcome,
            _strings,
            _lightTheme,
            _resultCommandRequested,
            CopyTrackInfo);
        _resultRipples.AddRange(OverlayVisualResources.AttachControlRipples(_visual.ResultHost));
        if (outcome.Status != MusicRecognitionStatus.Matched || !_animationsEnabled()) return;

        _matchRippleOperation = _root.Dispatcher.BeginInvoke(() =>
        {
            _matchRippleOperation = null;
            if (_disposed || _getMode() != OverlayInteractionMode.MusicResult) return;
            card.UpdateLayout();
            var origin = card.TransformToAncestor(_root).Transform(
                new Point(card.ActualWidth / 2, card.ActualHeight / 2));
            _effects.SceneRipples.Emit(new SceneRippleRequest(origin, SceneRipplePreset.MusicMatch, 1));
        }, DispatcherPriority.Loaded);
    }

    internal void DismissResult()
    {
        if (_disposed) return;
        _visual.ResultHost.Visibility = Visibility.Collapsed;
        _visual.ResultHost.Children.Clear();
        DisposeResultRipples();
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
        foreach (var button in _debugButtons) button.Click -= OnDebugScenarioClick;
        _debugButtons.Clear();
        foreach (var timer in _copyTimers)
        {
            timer.Stop();
            timer.Tick -= OnRestoreCopy;
        }
        _copyTimers.Clear();
        if (_matchRippleOperation?.Status == DispatcherOperationStatus.Pending)
            _matchRippleOperation.Abort();
        _matchRippleOperation = null;
        DisposeResultRipples();
        _visual.Waveform.Dispose();
    }

    private void OnMusicButtonClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing) return;
        if (_getMode() == OverlayInteractionMode.Selecting) _startRequested();
        else _cancelRequested();
        e.Handled = true;
    }

    private void OnDebugScenarioClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing ||
            sender is not Button { Tag: MusicDebugScenario scenario }) return;
        MusicOverlayVisualPresenter.SetDebugScenario(_visual, scenario, _lightTheme);
        _debugScenarioSelected(scenario);
    }

    private void CopyTrackInfo(string text, Button button)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing) return;
        try
        {
            _setClipboard(text);
            MusicOverlayVisualPresenter.SetCopyConfirmed(button, confirmed: true, _strings, _lightTheme);
            var restore = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            restore.Tick += OnRestoreCopy;
            restore.Tag = button;
            _copyTimers.Add(restore);
            restore.Start();
        }
        catch
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
}
