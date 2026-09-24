using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsWindowView
{
    private static readonly string[] Pages = ["general", "hotkeys", "search", "text", "music", "about"];
    private readonly UiStrings _strings;
    private readonly SettingsWindowModel _model;
    private readonly List<Action> _restoreDefaults = [];
    private readonly List<SettingsDropdownMotion> _dropdowns = [];
    private readonly DispatcherTimer _statusTimer;
    private readonly SettingsScrollMotionController _scrollMotion;
    private readonly SettingsPageTransition _pageTransition;
    private readonly SettingsDialogMotion _dialogMotion;
    private IInputElement? _dialogOwner;
    private string? _pendingShortcut;
    private bool _loadingSettings;

    internal SettingsWindowView(UiStrings strings, bool lightTheme, string iconPath, SettingsWindowModel model)
    {
        _strings = strings;
        _model = model;
        Window = (Window)Application.LoadComponent(new Uri(
            "/CircleFlow;component/CTS/Shell/SettingsPreview/SettingsWindow.xaml", UriKind.Relative));
        ApplyPalette(PluginPalette.Settings(lightTheme));
        Window.Title = strings.SettingsWindowTitle;
        Window.Icon = BitmapFrame.Create(new Uri(iconPath),
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
        var navigationIndicator = new SettingsNavigationIndicator(Element<Grid>("NavigationHost"),
            Element<StackPanel>("NavigationItems"), Element<Border>("NavigationSelection"));
        _scrollMotion = new SettingsScrollMotionController(Element<ScrollViewer>("PageScroll"),
            (TranslateTransform)Element<StackPanel>("PageContent").RenderTransform);
        _pageTransition = new SettingsPageTransition(Element<ScrollViewer>("PageScroll"),
            Element<FrameworkElement>("PageTransitionSurface"),
            Pages.Select(page => Element<FrameworkElement>("Page_" + page)).ToArray());
        _dialogMotion = new SettingsDialogMotion(Element<Border>("DialogLayer"),
            Element<FrameworkElement>("DialogMotionSurface"), Element<Border>("DialogScrim"), FinishCloseDialog);
        Window.Closed += (_, _) =>
        {
            _statusTimer.Stop();
            scrolling.Dispose();
            navigationIndicator.Dispose();
            _scrollMotion.Dispose();
            _pageTransition.Dispose();
            _dialogMotion.Dispose();
            foreach (var dropdown in _dropdowns) dropdown.Dispose();
        };
        Window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        Window.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnNavigationChecked));
        Window.PreviewKeyDown += OnPreviewKeyDown;
        Element<TextBox>("ShortcutInput").PreviewKeyDown += RecordShortcut;

        var provider = Element<ComboBox>("Provider");
        foreach (var descriptor in model.Providers)
            provider.Items.Add(new ComboBoxItem { Content = descriptor.DisplayName, Tag = descriptor.Id });
        provider.SelectionChanged += OnProviderChanged;
        _dropdowns.Add(new SettingsDropdownMotion(provider));
        var textSearch = Element<ComboBox>("TextSearch");
        textSearch.Items.Add(new ComboBoxItem
        {
            Content = strings.SettingsPreviewText("match_image_search"), Tag = TextSearchEngines.MatchImageSearch,
        });
        foreach (var engine in TextSearchEngines.All)
            textSearch.Items.Add(new ComboBoxItem { Content = strings.SettingsPreviewText("engine_" + engine.Id), Tag = engine.Id });
        textSearch.SelectionChanged += OnTextSearchChanged;
        _dropdowns.Add(new SettingsDropdownMotion(textSearch));
        var ocrLanguage = Element<ComboBox>("OcrLanguage");
        PopulateOcrLanguages();
        ocrLanguage.SelectionChanged += OnOcrLanguageChanged;
        _dropdowns.Add(new SettingsDropdownMotion(ocrLanguage));
        Element<TextBlock>("TranslationLanguage").Text = model.TranslationLanguageName;
        Element<TextBlock>("AppVersion").Text = strings.SettingsVersion(ProjectSupport.Version);
        var ignoreFullscreen = Element<CheckBox>("IgnoreFullscreen");
        ignoreFullscreen.Checked += OnIgnoreFullscreenChanged;
        ignoreFullscreen.Unchecked += OnIgnoreFullscreenChanged;

        // These controls have no application setting yet, so their values live only in this window.
        foreach (var name in new[] { "Launch", "ToolbarAsk", "ToolbarCopy", "ToolbarSave", "ToolbarTranslate" })
        {
            var control = Element<CheckBox>(name);
            var initial = control.IsChecked;
            _restoreDefaults.Add(() => control.IsChecked = initial);
        }
        foreach (var name in new[] { "AppLanguage", "Cleanup" })
        {
            var control = Element<ComboBox>(name);
            _dropdowns.Add(new SettingsDropdownMotion(control));
            var initial = control.SelectedIndex;
            _restoreDefaults.Add(() => control.SelectedIndex = initial);
        }
        LoadSettings();
        // The provider can change from the selection toolbar and the audio output from Windows while this window stays open.
        Window.Activated += (_, _) => LoadSettings();
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
        if (Element<FrameworkElement>("PageContent").IsKeyboardFocusWithin)
            Element<RadioButton>("Nav_" + page).Focus();
        _pageTransition.Show(Element<FrameworkElement>("Page_" + page));
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
                var saved = _model.ChangeHotkey(_pendingShortcut);
                LoadSettings();
                CloseDialog();
                ShowStatus(saved);
                break;
            case "confirm-reset":
                foreach (var restore in _restoreDefaults) restore();
                var reset = _model.ResetToDefaults();
                LoadSettings();
                CloseDialog();
                if (reset is not null) ShowStatus(reset);
                break;
            case "github": _model.Project.OpenRepository(); break;
            case "feedback": _model.Project.OpenFeedback(); break;
            case "license": _model.Project.OpenLicense(); break;
            case "donate": _model.Project.Open(); break;
            case "folder": _model.OpenDataFolder(); break;
            case "logs": _model.OpenLogsFolder(); break;
            case "ocr-languages": _model.OpenOcrLanguageSettings(); break;
            case "preview": ShowStatus(_strings.SettingsPreviewText("preview_action")); break;
        }
        e.Handled = true;
    }

    private void OpenDialog(bool shortcut)
    {
        _scrollMotion.Reset();
        HideStatus();
        if (Element<Border>("DialogLayer").Visibility != Visibility.Visible)
            _dialogOwner = Keyboard.FocusedElement;
        _pendingShortcut = null;
        Element<Grid>("Workspace").IsEnabled = false;
        Element<StackPanel>("ShortcutDialog").Visibility = shortcut ? Visibility.Visible : Visibility.Collapsed;
        Element<StackPanel>("ResetDialog").Visibility = shortcut ? Visibility.Collapsed : Visibility.Visible;
        Element<Button>("SaveShortcut").Visibility = shortcut ? Visibility.Visible : Visibility.Collapsed;
        Element<Button>("SaveShortcut").IsEnabled = false;
        Element<Button>("ConfirmReset").Visibility = shortcut ? Visibility.Collapsed : Visibility.Visible;
        _dialogMotion.Open();
        var input = Element<TextBox>("ShortcutInput");
        input.Text = _strings.SettingsPreviewText("press_shortcut");
        if (shortcut) input.Focus();
        else Element<Border>("DialogCard").MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void CloseDialog()
    {
        _pendingShortcut = null;
        _dialogMotion.Close();
    }

    private void FinishCloseDialog()
    {
        Element<Grid>("Workspace").IsEnabled = true;
        if (Window.IsVisible && _dialogOwner is not null) Keyboard.Focus(_dialogOwner);
        _dialogOwner = null;
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
        if (!_dialogMotion.IsOpen) return;
        if (e.Key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        _pendingShortcut = ShortcutGesture(key, Keyboard.Modifiers);
        Element<Button>("SaveShortcut").IsEnabled = _pendingShortcut is not null;
        Element<TextBox>("ShortcutInput").Text = _pendingShortcut is null
            ? _strings.SettingsShortcutInvalid
            : string.Join(" + ", _pendingShortcut.Split('+').Select(ShortcutLabel));
    }

    internal static string? ShortcutGesture(Key key, ModifierKeys modifiers)
    {
        var validKey = key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or Key.Space;
        if (!validKey || modifiers == ModifierKeys.None || modifiers.HasFlag(ModifierKeys.Windows)) return null;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        return string.Join('+', parts);
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            Select(Element<ComboBox>("Provider"), _model.ProviderId);
            Select(Element<ComboBox>("TextSearch"), _model.TextSearchEngineId);
            if (_model.RefreshOcrLanguages()) PopulateOcrLanguages();
            Select(Element<ComboBox>("OcrLanguage"), _model.OcrLanguageTag);
            Element<CheckBox>("IgnoreFullscreen").IsChecked = _model.IgnoreHotkeyInFullscreen;
        }
        finally { _loadingSettings = false; }
        var keys = _model.HotkeyGesture.Split('+')
            .Select((token, index) => new ShortcutPart(ShortcutLabel(token), index > 0)).ToArray();
        Element<ItemsControl>("ShortcutKeys").ItemsSource = keys;
        Element<ItemsControl>("HeroShortcutKeys").ItemsSource = keys;
        Element<TextBlock>("RuntimeVersion").Text = _model.WebViewRuntimeVersion ?? _strings.SettingsRuntimeMissing;
        var audioOutput = Element<TextBlock>("AudioOutput");
        audioOutput.Text = _model.AudioOutputName ?? _strings.SettingsPreviewText("audio_output_missing");
        audioOutput.ToolTip = audioOutput.Text;
    }

    private static void Select(ComboBox comboBox, string id) =>
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, id));

    private void OnTextSearchChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || Element<ComboBox>("TextSearch").SelectedItem is not ComboBoxItem { Tag: string id }) return;
        if (_model.SelectTextSearchEngine(id)) return;
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void PopulateOcrLanguages()
    {
        var comboBox = Element<ComboBox>("OcrLanguage");
        comboBox.Items.Clear();
        comboBox.Items.Add(new ComboBoxItem { Content = _strings.SettingsPreviewText("match_keyboard"), Tag = string.Empty });
        foreach (var language in _model.OcrLanguages)
            comboBox.Items.Add(new ComboBoxItem { Content = language.DisplayName, Tag = language.Tag });
        comboBox.IsEnabled = _model.OcrLanguages.Count != 0;
        Element<TextBlock>("OcrLanguageSubtitle").Text =
            _strings.SettingsPreviewText(comboBox.IsEnabled ? "ocr_language_sub" : "ocr_language_none");
    }

    private void OnOcrLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || Element<ComboBox>("OcrLanguage").SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (_model.SelectOcrLanguage(tag)) return;
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnIgnoreFullscreenChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (_model.SelectIgnoreHotkeyInFullscreen(Element<CheckBox>("IgnoreFullscreen").IsChecked == true)) return;
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || Element<ComboBox>("Provider").SelectedItem is not ComboBoxItem { Tag: string id }) return;
        if (!_model.SelectProvider(id)) LoadSettings();
    }

    private string ShortcutLabel(string token) => token switch
    {
        "Ctrl" or "Alt" or "Shift" or "Space" or "Win" => _strings.SettingsPreviewText(token.ToLowerInvariant()),
        _ => token,
    };

    private void ShowStatus(string text)
    {
        Element<TextBlock>("StatusText").Text = text;
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
