using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Interop;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsWindowView
{
    private static readonly string[] Pages = ["general", "hotkeys", "search", "text", "music", "about"];
    private readonly UiStrings _strings;
    private readonly List<Action> _restoreDefaults = [];
    private readonly DispatcherTimer _statusTimer;
    private readonly SettingsScrollMotionController _scrollMotion;
    private IInputElement? _dialogOwner;
    private string[]? _pendingShortcut;

    internal SettingsWindowView(UiStrings strings, bool lightTheme)
    {
        _strings = strings;
        Window = (Window)Application.LoadComponent(new Uri(
            "/CircleFlow;component/CTS/Shell/SettingsPreview/SettingsWindow.xaml", UriKind.Relative));
        ApplyPalette(PluginPalette.Settings(lightTheme));
        Window.Title = strings.SettingsWindowTitle;
        Window.Icon = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "Images", "app.ico")),
            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Window.SourceInitialized += (_, _) =>
        {
            var darkMode = lightTheme ? 0 : 1;
            NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(Window).Handle,
                NativeMethods.DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        };
        Window.DataContext = new PreviewText(strings);
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background, Window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _statusTimer.Tick += (_, _) => HideStatus();
        var scrolling = new SettingsScrollController(Element<ScrollViewer>("PageScroll"));
        _scrollMotion = new SettingsScrollMotionController(Element<ScrollViewer>("PageScroll"),
            (TranslateTransform)Element<StackPanel>("PageContent").RenderTransform);
        Window.Closed += (_, _) =>
        {
            _statusTimer.Stop();
            scrolling.Dispose();
            _scrollMotion.Dispose();
        };
        Window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        Window.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnNavigationChecked));
        Window.PreviewKeyDown += OnPreviewKeyDown;
        Element<TextBox>("ShortcutInput").PreviewKeyDown += RecordShortcut;

        // The preview owns only control values. No application settings or services enter this view.
        foreach (var name in new[] { "Launch", "IgnoreFullscreen" })
        {
            var control = Element<CheckBox>(name);
            var initial = control.IsChecked;
            _restoreDefaults.Add(() => control.IsChecked = initial);
        }
        foreach (var name in new[] { "AppLanguage", "Cleanup", "Provider", "OcrLanguage", "TargetLanguage" })
        {
            var control = Element<ComboBox>(name);
            var initial = control.SelectedIndex;
            _restoreDefaults.Add(() => control.SelectedIndex = initial);
        }
        foreach (var name in new[] { "Maximum", "Padding", "Delay" })
        {
            var control = Element<TextBox>(name);
            var initial = control.Text;
            _restoreDefaults.Add(() => control.Text = initial);
        }
        SetShortcut(DefaultShortcut());
    }

    internal Window Window { get; }

    private T Element<T>(string name) where T : FrameworkElement => (T)Window.FindName(name);

    private void ApplyPalette(SettingsPalette palette)
    {
        (string Key, Color Color)[] colors =
        [
            ("Paper", palette.Paper), ("Surface", palette.Surface), ("Card", palette.Card),
            ("Sidebar", palette.Sidebar), ("Text", palette.Text),
            ("ScrollbarThumb", palette.ScrollbarThumb),
            ("Muted", palette.Muted), ("Accent", palette.Accent), ("Line", palette.Line),
            ("Hover", palette.Hover), ("Selected", palette.Selected), ("Wash", palette.Wash),
            ("HeroStart", palette.HeroStart), ("HeroEnd", palette.HeroEnd),
            ("AccentLine", palette.AccentLine), ("Scrim", palette.Scrim),
            ("Transparent", PluginPalette.Transparent),
        ];
        foreach (var (key, color) in colors)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Window.Resources["Settings" + key] = brush;
        }
        Window.Resources["SettingsHeroStartColor"] = palette.HeroStart;
        Window.Resources["SettingsHeroEndColor"] = palette.HeroEnd;
        Window.Resources["SettingsScrollTrackWidth"] = (double)OverlayScrollbarPolicy.TrackWidthPixels;
        Window.Resources["SettingsScrollThumbWidth"] = (double)OverlayScrollbarPolicy.ThumbWidthPixels;
        Window.Resources["SettingsScrollMinThumbHeight"] = (double)OverlayScrollbarPolicy.MinimumThumbHeightPixels;
        Window.Resources["SettingsScrollTrackMargin"] = new Thickness(0, OverlayScrollbarPolicy.EdgeInsetPixels, 0, OverlayScrollbarPolicy.EdgeInsetPixels);
        Window.Resources["SettingsScrollThumbMargin"] = new Thickness(0, 0, OverlayScrollbarPolicy.EdgeInsetPixels, 0);
    }

    private void OnNavigationChecked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is RadioButton { Tag: string page } && Pages.Contains(page))
            ShowPage(page);
    }

    private void ShowPage(string page)
    {
        _scrollMotion.Reset();
        foreach (var name in Pages)
            Element<StackPanel>("Page_" + name).Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        Element<ScrollViewer>("PageScroll").ScrollToTop();
        HideStatus();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { Tag: string action }) return;
        if (action.StartsWith("page:", StringComparison.Ordinal))
        {
            var nav = Element<RadioButton>("Nav_" + action[5..]);
            nav.IsChecked = true;
            nav.Focus();
            return;
        }
        switch (action)
        {
            case "edit": OpenDialog(shortcut: true); break;
            case "reset": OpenDialog(shortcut: false); break;
            case "cancel": CloseDialog(); break;
            case "save" when _pendingShortcut is not null:
                SetShortcut(_pendingShortcut);
                CloseDialog();
                ShowStatus("preview_shortcut");
                break;
            case "confirm-reset":
                foreach (var restore in _restoreDefaults) restore();
                SetShortcut(DefaultShortcut());
                CloseDialog();
                ShowStatus("preview_reset");
                break;
            case "preview": ShowStatus("preview_action"); break;
        }
        e.Handled = true;
    }

    private void OpenDialog(bool shortcut)
    {
        _scrollMotion.Reset();
        HideStatus();
        _dialogOwner = Keyboard.FocusedElement;
        _pendingShortcut = null;
        Element<Grid>("Workspace").IsEnabled = false;
        Element<StackPanel>("ShortcutDialog").Visibility = shortcut ? Visibility.Visible : Visibility.Collapsed;
        Element<StackPanel>("ResetDialog").Visibility = shortcut ? Visibility.Collapsed : Visibility.Visible;
        Element<Button>("SaveShortcut").Visibility = shortcut ? Visibility.Visible : Visibility.Collapsed;
        Element<Button>("SaveShortcut").IsEnabled = false;
        Element<Button>("ConfirmReset").Visibility = shortcut ? Visibility.Collapsed : Visibility.Visible;
        Element<Border>("DialogLayer").Visibility = Visibility.Visible;
        var input = Element<TextBox>("ShortcutInput");
        input.Text = _strings.SettingsPreviewText("press_shortcut");
        if (shortcut) input.Focus();
        else Element<Border>("DialogCard").MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void CloseDialog()
    {
        Element<Border>("DialogLayer").Visibility = Visibility.Collapsed;
        Element<Grid>("Workspace").IsEnabled = true;
        if (_dialogOwner is not null) Keyboard.Focus(_dialogOwner);
        _pendingShortcut = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Element<Border>("DialogLayer").Visibility != Visibility.Visible) return;
        CloseDialog();
        e.Handled = true;
    }

    private void RecordShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        var modifiers = Keyboard.Modifiers;
        var validKey = key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or Key.Space;
        var save = Element<Button>("SaveShortcut");
        if (!validKey || modifiers == ModifierKeys.None || modifiers.HasFlag(ModifierKeys.Windows))
        {
            _pendingShortcut = null;
            save.IsEnabled = false;
            Element<TextBox>("ShortcutInput").Text = _strings.SettingsPreviewText("invalid_shortcut");
            return;
        }
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add(_strings.SettingsPreviewText("ctrl"));
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(_strings.SettingsPreviewText("alt"));
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(_strings.SettingsPreviewText("shift"));
        parts.Add(key == Key.Space ? _strings.SettingsPreviewText("space") :
            key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        _pendingShortcut = parts.ToArray();
        Element<TextBox>("ShortcutInput").Text = string.Join(" + ", parts);
        save.IsEnabled = true;
    }

    private string[] DefaultShortcut() =>
        [_strings.SettingsPreviewText("ctrl"), _strings.SettingsPreviewText("alt"), _strings.SettingsPreviewText("space")];

    private void SetShortcut(string[] parts)
    {
        var keys = parts.Select((label, index) => new ShortcutPart(label, index > 0)).ToArray();
        Element<ItemsControl>("ShortcutKeys").ItemsSource = keys;
        Element<ItemsControl>("HeroShortcutKeys").ItemsSource = keys;
    }

    private void ShowStatus(string key)
    {
        Element<TextBlock>("StatusText").Text = _strings.SettingsPreviewText(key);
        Element<Border>("StatusBanner").Visibility = Visibility.Visible;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void HideStatus()
    {
        _statusTimer.Stop();
        Element<Border>("StatusBanner").Visibility = Visibility.Collapsed;
    }

    private sealed class PreviewText(UiStrings strings)
    {
        public string this[string key] => strings.SettingsPreviewText(key);
    }

    private sealed record ShortcutPart(string Label, bool HasSeparator);
}
