using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsWindowView
{
    private static readonly string[] Pages = ["general", "hotkeys", "search", "text", "music", "about", "developer"];
    private static readonly (string Name, SelectionToolbarAction Action)[] ToolbarActions =
    [
        ("ToolbarAsk", SelectionToolbarAction.Ask), ("ToolbarCopy", SelectionToolbarAction.Copy),
        ("ToolbarSave", SelectionToolbarAction.Save), ("ToolbarTranslate", SelectionToolbarAction.Translate),
    ];
    private readonly UiStrings _strings;
    private readonly SettingsWindowModel _model;
    private readonly List<(ComboBox ComboBox, SettingsDropdownMotion Motion)> _dropdowns = [];
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
        SettingsWindowTheme.Apply(Window, lightTheme, iconPath);
        ApplyScrollbarMetrics();
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
            foreach (var (_, motion) in _dropdowns) motion.Dispose();
        };
        Window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        Window.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnNavigationChecked));
        Window.PreviewKeyDown += OnPreviewKeyDown;
        Element<TextBox>("ShortcutInput").PreviewKeyDown += RecordShortcut;
        Element<TextBlock>("AppVersion").MouseLeftButtonDown += OnVersionClicked;
        if (model.DeveloperSettingsUnlocked) Element<RadioButton>("Nav_developer").Visibility = Visibility.Visible;

        var provider = Element<ComboBox>("Provider");
        foreach (var descriptor in model.Providers)
            provider.Items.Add(new ComboBoxItem { Tag = descriptor.Id });
        provider.SelectionChanged += OnProviderChanged;
        AddDropdown(provider);
        var textSearch = Element<ComboBox>("TextSearch");
        textSearch.Items.Add(new ComboBoxItem { Tag = TextSearchEngines.MatchImageSearch });
        foreach (var engine in TextSearchEngines.All)
            textSearch.Items.Add(new ComboBoxItem { Tag = engine.Id });
        textSearch.SelectionChanged += OnTextSearchChanged;
        AddDropdown(textSearch);
        var cleanup = Element<ComboBox>("Cleanup");
        foreach (var days in BrowserDataCleanup.IntervalDays)
            cleanup.Items.Add(new ComboBoxItem { Tag = days });
        cleanup.Items.Add(new ComboBoxItem { Tag = BrowserDataCleanup.Never });
        cleanup.SelectionChanged += OnCleanupChanged;
        AddDropdown(cleanup);
        var ocrLanguage = Element<ComboBox>("OcrLanguage");
        PopulateOcrLanguages();
        ocrLanguage.SelectionChanged += OnOcrLanguageChanged;
        AddDropdown(ocrLanguage);
        Element<TextBlock>("TranslationLanguage").Text = model.TranslationLanguageName;
        var textSearchBrowser = Element<CheckBox>("TextSearchBrowser");
        textSearchBrowser.Checked += OnTextSearchBrowserChanged;
        textSearchBrowser.Unchecked += OnTextSearchBrowserChanged;
        var ignoreFullscreen = Element<CheckBox>("IgnoreFullscreen");
        ignoreFullscreen.Checked += OnIgnoreFullscreenChanged;
        ignoreFullscreen.Unchecked += OnIgnoreFullscreenChanged;
        var launch = Element<CheckBox>("Launch");
        launch.Checked += OnLaunchChanged;
        launch.Unchecked += OnLaunchChanged;

        foreach (var (name, action) in ToolbarActions)
        {
            var toggle = Element<CheckBox>(name);
            toggle.Tag = action;
            toggle.Checked += OnToolbarActionChanged;
            toggle.Unchecked += OnToolbarActionChanged;
        }

        var appLanguage = Element<ComboBox>("AppLanguage");
        appLanguage.Items.Add(new ComboBoxItem { Tag = string.Empty });
        foreach (var language in model.AppLanguages)
            appLanguage.Items.Add(new ComboBoxItem { Content = language.DisplayName, Tag = language.Tag });
        Element<Border>("AppLanguageRow").Visibility = model.AppLanguages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        appLanguage.SelectionChanged += OnAppLanguageChanged;
        AddDropdown(appLanguage);
        ApplyTexts();
        LoadSettings();
        // The provider can change from the selection toolbar, and the audio output and startup entry from Windows, while this window stays open.
        Window.Activated += (_, _) => LoadSettings();
    }

    internal Window Window { get; }

    private T Element<T>(string name) where T : FrameworkElement => (T)Window.FindName(name);

    private void ApplyScrollbarMetrics()
    {
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
                var reset = _model.ResetToDefaults();
                RefreshLanguage();
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
            case "show-onboarding": _model.ShowOnboarding(); break;
        }
        e.Handled = true;
    }

    // Like Android's build number: three quick clicks on the version reveal the developer page.
    private void OnVersionClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 3) return;
        _model.UnlockDeveloperSettings();
        Element<RadioButton>("Nav_developer").Visibility = Visibility.Visible;
        ShowStatus(_strings.SettingsPreviewText("developer_unlocked"));
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
        if (e.Key is Key.Tab or Key.Escape || ShortcutText.ClosesWindow(e)) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ShortcutText.IsModifier(key)) return;
        _pendingShortcut = ShortcutText.Gesture(key, Keyboard.Modifiers);
        Element<Button>("SaveShortcut").IsEnabled = _pendingShortcut is not null;
        Element<TextBox>("ShortcutInput").Text = _pendingShortcut is null
            ? _strings.SettingsShortcutInvalid
            : string.Join(" + ", ShortcutText.Labels(_pendingShortcut, _strings));
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            Select(Element<ComboBox>("Provider"), _model.ProviderId);
            foreach (var item in Items("TextSearch"))
            {
                var available = TextSearchEngines.IsAvailable((string)item.Tag, _model.TextSearchInBuiltInBrowser,
                    _model.BrowserDataCleanupDays);
                item.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
                item.IsEnabled = available;
            }
            Select(Element<ComboBox>("TextSearch"), _model.TextSearchEngineId);
            Element<CheckBox>("TextSearchBrowser").IsChecked = _model.TextSearchInBuiltInBrowser;
            if (_model.RefreshOcrLanguages()) PopulateOcrLanguages();
            Select(Element<ComboBox>("OcrLanguage"), _model.OcrLanguageTag);
            Select(Element<ComboBox>("AppLanguage"), _model.AppLanguageTag);
            Element<CheckBox>("IgnoreFullscreen").IsChecked = _model.IgnoreHotkeyInFullscreen;
            Element<CheckBox>("Launch").IsChecked = _model.LaunchAtStartup;
            var cleanup = Element<ComboBox>("Cleanup");
            cleanup.SelectedItem = cleanup.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, _model.BrowserDataCleanupDays));
            foreach (var (name, action) in ToolbarActions)
                Element<CheckBox>(name).IsChecked = _model.IsToolbarActionShown(action);
        }
        finally { _loadingSettings = false; }
        var keys = ShortcutText.Labels(_model.HotkeyGesture, _strings)
            .Select((label, index) => new ShortcutPart(label, index > 0)).ToArray();
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

    // Runs again after the app language changes, so it sets every text that XAML bindings do not cover.
    private void ApplyTexts()
    {
        Window.Title = _strings.SettingsWindowTitle;
        Window.DataContext = new PreviewText(_strings);
        foreach (var (item, descriptor) in Items("Provider").Zip(_model.Providers))
            item.Content = descriptor.DisplayName;
        foreach (var item in Items("TextSearch"))
            item.Content = _strings.SettingsPreviewText(Equals(item.Tag, TextSearchEngines.MatchImageSearch)
                ? "match_image_search" : "engine_" + item.Tag);
        foreach (var item in Items("Cleanup"))
            item.Content = _strings.SettingsPreviewText(Equals(item.Tag, BrowserDataCleanup.Never) ? "never" : "every_" + item.Tag);
        Items("AppLanguage")[0].Content = _strings.SettingsPreviewText("match_system");
        ApplyOcrTexts();
        Element<TextBlock>("AppVersion").Text = _strings.SettingsVersion(ProjectSupport.Version);
    }

    private void AddDropdown(ComboBox comboBox) => _dropdowns.Add((comboBox, new SettingsDropdownMotion(comboBox)));

    private ComboBoxItem[] Items(string comboBox) => Element<ComboBox>(comboBox).Items.OfType<ComboBoxItem>().ToArray();

    private void PopulateOcrLanguages()
    {
        var comboBox = Element<ComboBox>("OcrLanguage");
        comboBox.Items.Clear();
        comboBox.Items.Add(new ComboBoxItem { Tag = string.Empty });
        foreach (var language in _model.OcrLanguages)
            comboBox.Items.Add(new ComboBoxItem { Content = language.DisplayName, Tag = language.Tag });
        comboBox.IsEnabled = _model.OcrLanguages.Count != 0;
        ApplyOcrTexts();
    }

    private void ApplyOcrTexts()
    {
        Items("OcrLanguage")[0].Content = _strings.SettingsPreviewText("match_keyboard");
        Element<TextBlock>("OcrLanguageSubtitle").Text = _strings.SettingsPreviewText(
            Element<ComboBox>("OcrLanguage").IsEnabled ? "ocr_language_sub" : "ocr_language_none");
    }

    private void OnOcrLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || Element<ComboBox>("OcrLanguage").SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (_model.SelectOcrLanguage(tag)) return;
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnAppLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || Element<ComboBox>("AppLanguage").SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (_model.SelectAppLanguage(tag))
        {
            RefreshLanguage();
            return;
        }
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void RefreshLanguage()
    {
        HideStatus();
        ApplyTexts();
        _loadingSettings = true;
        try
        {
            // A combo box shows a copy of the selected content, so reselecting shows the translated text.
            foreach (var (comboBox, _) in _dropdowns)
            {
                var selected = comboBox.SelectedItem;
                comboBox.SelectedItem = null;
                comboBox.SelectedItem = selected;
            }
        }
        finally { _loadingSettings = false; }
        LoadSettings();
    }

    private void OnTextSearchBrowserChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var saved = _model.SelectTextSearchInBuiltInBrowser(Element<CheckBox>("TextSearchBrowser").IsChecked == true);
        LoadSettings();
        if (!saved) ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnIgnoreFullscreenChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (_model.SelectIgnoreHotkeyInFullscreen(Element<CheckBox>("IgnoreFullscreen").IsChecked == true)) return;
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnLaunchChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (_model.SelectLaunchAtStartup(Element<CheckBox>("Launch").IsChecked == true)) return;
        LoadSettings();
        ShowStatus(_strings.SettingsPreviewText("launch_failed"));
    }

    private void OnCleanupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || Element<ComboBox>("Cleanup").SelectedItem is not ComboBoxItem { Tag: int days }) return;
        var saved = _model.SelectBrowserDataCleanup(days);
        LoadSettings();
        if (!saved) ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnToolbarActionChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || sender is not CheckBox { Tag: SelectionToolbarAction action } toggle) return;
        if (_model.ShowToolbarAction(action, toggle.IsChecked == true)) return;
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || Element<ComboBox>("Provider").SelectedItem is not ComboBoxItem { Tag: string id }) return;
        if (!_model.SelectProvider(id)) LoadSettings();
    }

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
