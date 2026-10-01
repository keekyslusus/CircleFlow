using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CircleToSearch.Interop;
using CircleToSearch.Shell.SettingsPreview;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.Onboarding;

internal sealed class OnboardingWindowView
{
    private const int LastStep = 2;
    private static readonly TimeSpan DotDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultStatusLifetime = TimeSpan.FromSeconds(5);
    private readonly UiStrings _strings;
    private readonly OnboardingModel _model;
    private readonly OnboardingStepTransition _steps;
    private readonly OnboardingSwapTransition _hotkeyEntry;
    private readonly OnboardingSwapTransition _shortcutAction;
    private readonly OnboardingFadeTransition _status;
    private readonly OnboardingSwapTransition _launchFailure;
    private readonly DispatcherTimer _statusTimer;

    internal OnboardingWindowView(UiStrings strings, bool lightTheme, string iconPath, OnboardingModel model,
        TimeSpan? statusLifetime = null)
    {
        _strings = strings;
        _model = model;
        Window = (Window)Application.LoadComponent(new Uri(
            "/CircleFlow;component/CTS/Shell/Onboarding/OnboardingWindow.xaml", UriKind.Relative));
        SettingsWindowTheme.Apply(Window, lightTheme, iconPath);
        ApplyPalette(PluginPalette.Onboarding(lightTheme));
        Window.Title = strings.OnboardingText("window_title");
        Window.DataContext = new OnboardingText(strings);
        Element<TextBlock>("LensLabel").Text = strings.GoogleLensProviderName;
        Element<TextBlock>("CopyLabel").Text = strings.TextCopy;
        Element<TextBlock>("SearchLabel").Text = strings.TextSearch;
        Element<TextBlock>("AltLabel").Text = strings.AltKeyName;
        Element<TextBlock>("AltPlus").Text = strings.SettingsPreviewText("plus");
        Element<ItemsControl>("ToolbarIcons").ItemsSource = new[]
        {
            PluginIcons.SparkleOutlined, PluginIcons.CopyOutlined, PluginIcons.DownloadOutlined, PluginIcons.TranslateOutlined,
        };
        _steps = new OnboardingStepTransition(Element<FrameworkElement>("Illustration"),
            Enumerable.Range(1, LastStep + 1)
                .Select(step => new[] { Element<FrameworkElement>($"Step{step}Art"), Element<FrameworkElement>($"Step{step}Copy") })
                .ToArray());
        _hotkeyEntry = new OnboardingSwapTransition(Element<FrameworkElement>("HotkeyKeys"), Element<FrameworkElement>("RecordingPrompt"));
        _shortcutAction = new OnboardingSwapTransition(Element<FrameworkElement>("ChangeShortcutContent"),
            Element<FrameworkElement>("CancelShortcutContent"));
        _status = new OnboardingFadeTransition(Element<FrameworkElement>("HotkeyStatus"), shown: false);
        _launchFailure = new OnboardingSwapTransition(Element<FrameworkElement>("Step3Text"), Element<FrameworkElement>("LaunchFailed"));
        Element<TextBlock>("LaunchFailed").Text = strings.SettingsPreviewText("launch_failed");
        var launch = Element<CheckBox>("Launch");
        launch.Checked += (_, _) => _launchFailure.Show(second: false);
        launch.Unchecked += (_, _) => _launchFailure.Show(second: false);
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background, Window.Dispatcher)
        {
            Interval = statusLifetime ?? DefaultStatusLifetime,
        };
        _statusTimer.Tick += (_, _) => HideStatus();
        Window.Closed += (_, _) => _statusTimer.Stop();
        foreach (var (dot, index) in Dots().Select((dot, index) => (dot, index)))
            AutomationProperties.SetName(dot, strings.OnboardingStep(index + 1, LastStep + 1));
        Window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        Window.PreviewKeyDown += OnPreviewKeyDown;
        ShowHotkey();
        ExplainInactiveHotkey();
        ShowStep(0);
    }

    internal Window Window { get; }

    private T Element<T>(string name) where T : FrameworkElement => (T)Window.FindName(name);

    private void ApplyPalette(OnboardingPalette palette)
    {
        var screen = PluginPalette.OnboardingScreen;
        (string Key, Color Color)[] colors =
        [
            ("InactiveStep", palette.InactiveStep), ("OnAccent", palette.OnAccent),
            ("ToolbarSurface", palette.ToolbarSurface), ("ToolbarText", palette.ToolbarText),
            ("ToolbarBorder", palette.ToolbarBorder), ("ChipSurface", palette.ChipSurface),
            ("ChipText", palette.ChipText), ("ChipBorder", palette.ChipBorder), ("TextHighlight", palette.TextHighlight),
            ("ScreenSurface", screen.Surface), ("ScreenBorder", screen.Border), ("ScreenHeading", screen.Heading),
            ("ScreenLine", screen.Line), ("ScreenText", screen.Text), ("ScreenSelection", screen.Selection),
            ("ScreenDim", screen.Dim),
        ];
        foreach (var (key, color) in colors) Window.Resources["Onboarding" + key] = SettingsWindowTheme.Frozen(color);
        Window.Resources["OnboardingShadowColor"] = screen.Shadow;
        Window.Resources["OnboardingGlowColor"] = screen.Selection;
        Window.Resources["TwinDrillEgg"] = TwinDrillEgg.Create();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { Tag: string action }) return;
        switch (action)
        {
            case "next": ShowStep(_steps.Current + 1); break;
            case "back": ShowStep(_steps.Current - 1); break;
            case var step when step.StartsWith("step:", StringComparison.Ordinal): ShowStep(int.Parse(step[5..])); break;
            case "skip": Window.Close(); break;
            case "change-shortcut":
                if (_hotkeyEntry.ShowsSecond) StopRecording();
                else StartRecording();
                break;
            case "try":
                // The user asked for this explicitly, so a failure keeps the wizard open to say so.
                if (_model.SelectLaunchAtStartup(Element<CheckBox>("Launch").IsChecked == true))
                {
                    // The native close fade outlasts the capture's hide delay and would end up in the screenshot.
                    var disabled = 1;
                    NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(Window).Handle,
                        NativeMethods.DwmwaTransitionsForceDisabled, ref disabled, sizeof(int));
                    Window.Close();
                    _model.OpenOverlay();
                }
                else _launchFailure.Show(second: true);
                break;
        }
        e.Handled = true;
    }

    private IEnumerable<Button> Dots() => Element<StackPanel>("Steps").Children.OfType<Button>();

    private void ShowStep(int step)
    {
        step = Math.Clamp(step, 0, LastStep);
        StopRecording();
        _steps.Show(step);
        var animate = UiAnimationPolicy.Enabled && Window.IsLoaded;
        foreach (var (dot, index) in Dots().Select((dot, index) => ((Border)dot.Content, index)))
        {
            var width = index == step ? 18 : 6;
            dot.BeginAnimation(FrameworkElement.WidthProperty, animate
                ? new DoubleAnimation(width, DotDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }
                : null);
            dot.Width = width;
            dot.SetResourceReference(Border.BackgroundProperty, index == step ? "SettingsAccent" : "OnboardingInactiveStep");
        }
        Element<Button>("Skip").Visibility = Show(step == 0);
        Element<Button>("Back").Visibility = Show(step == 1);
        Element<Button>("Next").Visibility = Show(step < LastStep);
        Element<Button>("TryIt").Visibility = Show(step == LastStep);
        Element<CheckBox>("Launch").Visibility = Show(step == LastStep);
        Element<Button>(step == LastStep ? "TryIt" : "Next").Focus();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void StartRecording()
    {
        _hotkeyEntry.Show(second: true);
        _shortcutAction.Show(second: true);
        HideStatus();
    }

    private void StopRecording()
    {
        if (!_hotkeyEntry.ShowsSecond) return;
        _hotkeyEntry.Show(second: false);
        _shortcutAction.Show(second: false);
        ExplainInactiveHotkey();
    }

    // A shortcut that does not work stays explained until the user replaces it.
    private void ExplainInactiveHotkey()
    {
        if (!_model.HotkeyActive) ShowStatus(_strings.SettingsShortcutUnavailable(_model.HotkeyGesture), expires: false);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hotkeyEntry.ShowsSecond || e.Key == Key.Tab || ShortcutText.ClosesWindow(e)) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            StopRecording();
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ShortcutText.IsModifier(key)) return;
        if (ShortcutText.Gesture(key, Keyboard.Modifiers) is not { } gesture)
        {
            ShowStatus(_strings.SettingsShortcutInvalid, expires: true);
            return;
        }
        var message = _model.ChangeHotkey(gesture);
        StopRecording();
        ShowHotkey();
        ShowStatus(message, expires: _model.HotkeyActive);
    }

    private void ShowHotkey()
    {
        Element<ItemsControl>("HotkeyKeys").ItemsSource = ShortcutText.Keys(_model.HotkeyGesture, _strings);
    }

    private void ShowStatus(string text, bool expires)
    {
        Element<TextBlock>("HotkeyStatus").Text = text;
        _status.Show(true);
        _statusTimer.Stop();
        if (expires) _statusTimer.Start();
    }

    private void HideStatus()
    {
        _statusTimer.Stop();
        _status.Show(false);
    }

    private sealed class OnboardingText(UiStrings strings)
    {
        public string this[string key] => strings.OnboardingText(key);
    }
}
