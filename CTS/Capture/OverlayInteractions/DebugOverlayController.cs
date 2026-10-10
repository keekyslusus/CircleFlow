using System.Windows;
using System.Windows.Controls;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class DebugOverlayController : IDisposable
{
    private readonly DebugOverlayVisual _visual;
    private readonly bool _lightTheme;
    private readonly bool _debugEnabled;
    private readonly Func<OverlayInteractionMode> _getMode;
    private readonly Action<MusicDebugScenario> _musicScenarioSelected;
    private readonly Action<ToastNotification> _showToast;
    private readonly Action? _resetTranslationConsent;
    private readonly PinterestSimulation? _pinterestSimulation;
    private readonly UiStrings _strings;
    private readonly List<Button> _musicScenarioButtons = [];
    private readonly List<Button> _pinterestModeButtons = [];
    private readonly List<Button> _toastButtons = [];
    private bool _disposed;

    internal DebugOverlayController(
        DebugOverlayVisual visual,
        bool lightTheme,
        bool debugEnabled,
        Func<OverlayInteractionMode> getMode,
        Action<MusicDebugScenario> musicScenarioSelected,
        Action<ToastNotification> showToast,
        Action? resetTranslationConsent,
        UiStrings strings,
        PinterestSimulation? pinterestSimulation = null)
    {
        _visual = visual;
        _lightTheme = lightTheme;
        _debugEnabled = debugEnabled;
        _getMode = getMode;
        _musicScenarioSelected = musicScenarioSelected;
        _showToast = showToast;
        _resetTranslationConsent = resetTranslationConsent;
        _pinterestSimulation = pinterestSimulation;
        _strings = strings;
        _visual.ResetTranslationConsentButton.IsEnabled = resetTranslationConsent is not null;
        // The simulation outlives the session, so a new overlay shows the mode chosen in an earlier one.
        DebugOverlayVisualPresenter.SetPinterestMode(_visual, pinterestSimulation?.Mode ?? PinterestDebugMode.Live, lightTheme);

        foreach (var button in _visual.MusicScenarioButtons.Children.OfType<Button>())
        {
            button.Click += OnMusicScenarioClick;
            _musicScenarioButtons.Add(button);
        }
        foreach (var button in _visual.PinterestModeButtons.Children.OfType<Button>())
        {
            button.IsEnabled = pinterestSimulation is not null;
            button.Click += OnPinterestModeClick;
            _pinterestModeButtons.Add(button);
        }
        foreach (var button in _visual.ToastButtons.Children.OfType<Button>())
        {
            button.Click += OnToastClick;
            _toastButtons.Add(button);
        }
        _visual.ResetTranslationConsentButton.Click += OnResetTranslationConsentClick;
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
        foreach (var button in _pinterestModeButtons) button.Click -= OnPinterestModeClick;
        _pinterestModeButtons.Clear();
        foreach (var button in _toastButtons) button.Click -= OnToastClick;
        _toastButtons.Clear();
        _visual.ResetTranslationConsentButton.Click -= OnResetTranslationConsentClick;
    }

    private void OnMusicScenarioClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing ||
            sender is not Button { Tag: MusicDebugScenario scenario }) return;
        DebugOverlayVisualPresenter.SetMusicScenario(_visual, scenario, _lightTheme);
        _musicScenarioSelected(scenario);
        SetOpen(false);
    }

    private void OnPinterestModeClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _pinterestSimulation is null || _getMode() == OverlayInteractionMode.Closing ||
            sender is not Button { Tag: PinterestDebugMode mode }) return;
        _pinterestSimulation.Mode = mode;
        DebugOverlayVisualPresenter.SetPinterestMode(_visual, mode, _lightTheme);
        SetOpen(false);
    }

    private void OnToastClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _getMode() == OverlayInteractionMode.Closing ||
            sender is not Button { Tag: ToastTone tone, Content: string message }) return;
        _showToast(new ToastNotification(message, tone));
    }

    private void OnResetTranslationConsentClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || !_debugEnabled || !IsOpen || _getMode() == OverlayInteractionMode.Closing ||
            _resetTranslationConsent is null) return;
        try { _resetTranslationConsent(); }
        catch (Exception exception)
        {
            _showToast(new ToastNotification(_strings.SavingFailed(exception.Message), ToastTone.Error));
            return;
        }
        SetOpen(false);
        _showToast(new ToastNotification(_strings.DebugTranslationConsentReset, ToastTone.Success));
    }
}
