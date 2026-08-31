using System.Windows;
using System.Windows.Controls;
using CircleToSearch.MusicRecognition;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class DebugOverlayController : IDisposable
{
    private readonly DebugOverlayVisual _visual;
    private readonly bool _lightTheme;
    private readonly bool _debugEnabled;
    private readonly Func<OverlayInteractionMode> _getMode;
    private readonly Action<MusicDebugScenario> _musicScenarioSelected;
    private readonly Action<ToastNotification> _showToast;
    private readonly List<Button> _musicScenarioButtons = [];
    private readonly List<Button> _toastButtons = [];
    private bool _disposed;

    internal DebugOverlayController(
        DebugOverlayVisual visual,
        bool lightTheme,
        bool debugEnabled,
        Func<OverlayInteractionMode> getMode,
        Action<MusicDebugScenario> musicScenarioSelected,
        Action<ToastNotification> showToast)
    {
        _visual = visual;
        _lightTheme = lightTheme;
        _debugEnabled = debugEnabled;
        _getMode = getMode;
        _musicScenarioSelected = musicScenarioSelected;
        _showToast = showToast;

        foreach (var button in _visual.MusicScenarioButtons.Children.OfType<Button>())
        {
            button.Click += OnMusicScenarioClick;
            _musicScenarioButtons.Add(button);
        }
        foreach (var button in _visual.ToastButtons.Children.OfType<Button>())
        {
            button.Click += OnToastClick;
            _toastButtons.Add(button);
        }
    }

    internal bool IsOpen { get; private set; }

    internal void SetOpen(bool open)
    {
        if (_disposed || (open && (!_debugEnabled || _getMode() == OverlayInteractionMode.Closing))) return;
        IsOpen = open;
        _visual.Panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var button in _musicScenarioButtons) button.Click -= OnMusicScenarioClick;
        _musicScenarioButtons.Clear();
        foreach (var button in _toastButtons) button.Click -= OnToastClick;
        _toastButtons.Clear();
    }

    private void OnMusicScenarioClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing ||
            sender is not Button { Tag: MusicDebugScenario scenario }) return;
        DebugOverlayVisualPresenter.SetMusicScenario(_visual, scenario, _lightTheme);
        _musicScenarioSelected(scenario);
    }

    private void OnToastClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing ||
            sender is not Button { Tag: ToastTone tone, Content: string message }) return;
        _showToast(new ToastNotification(message, tone));
    }
}
