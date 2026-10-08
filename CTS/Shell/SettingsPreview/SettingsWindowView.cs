using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.Updates;

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
    private readonly SmoothScrollMotionController _scrollMotion;
    private readonly SettingsPageTransition _pageTransition;
    private readonly SettingsDialogMotion _dialogMotion;
    private readonly SettingsStatusMotion _statusMotion;
    private readonly SettingsMusicHistoryPanel _history;
    private readonly SettingsProviderMenuPanel _providerMenu;
    private readonly SettingsCollapseMotion _historyRetentionMotion;
    private readonly SettingsCollapseMotion _historyContentMotion;
    private readonly SettingsNavigationHistory _navigation;
    private readonly Action _shortcutKeyPressed;
    private IInputElement? _dialogOwner;
    private string? _pendingShortcut;
    private bool _loadingSettings;
    private bool _sidePressClosedDropdown;

    internal SettingsWindowView(UiStrings strings, bool lightTheme, string iconPath, SettingsWindowModel model,
        Func<Action<ToastNotification>, ClipboardCopyService> createClipboardCopy, Action? shortcutKeyPressed = null)
    {
        _strings = strings;
        _model = model;
        _shortcutKeyPressed = shortcutKeyPressed ?? (() => { });
        Window = (Window)Application.LoadComponent(new Uri(
            "/CircleFlow;component/CTS/Shell/SettingsPreview/SettingsWindow.xaml", UriKind.Relative));
        SettingsWindowTheme.Apply(Window, lightTheme, iconPath);
        ApplyScrollbarMetrics();
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background, Window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _statusTimer.Tick += (_, _) => HideStatus();
        _statusMotion = new SettingsStatusMotion(Element<Border>("StatusBanner"));
        var scrolling = new AutoHideScrollbarController(Element<ScrollViewer>("PageScroll"));
        var navigationIndicator = new SettingsNavigationIndicator(Element<Grid>("NavigationHost"),
            Element<StackPanel>("NavigationItems"), Element<Border>("NavigationSelection"));
        _scrollMotion = new SmoothScrollMotionController(Element<ScrollViewer>("PageScroll"),
            (TranslateTransform)Element<StackPanel>("PageContent").RenderTransform);
        _pageTransition = new SettingsPageTransition(Element<ScrollViewer>("PageScroll"),
            Element<FrameworkElement>("PageTransitionSurface"),
            Pages.Select(page => Element<FrameworkElement>("Page_" + page)).ToArray());
        _dialogMotion = new SettingsDialogMotion(Element<Border>("DialogLayer"),
            Element<FrameworkElement>("DialogMotionSurface"), Element<Border>("DialogScrim"), FinishCloseDialog);
        _history = new SettingsMusicHistoryPanel(Element<FrameworkElement>("HistoryContent"), model, strings,
            createClipboardCopy(toast => ShowStatus(toast.Message)));
        _providerMenu = new SettingsProviderMenuPanel(Element<FrameworkElement>("ProviderMenuDialog"), model, strings,
            lightTheme, ShowStatus, ShowProviderMenuSummary);
        _historyRetentionMotion = new SettingsCollapseMotion(Element<FrameworkElement>("HistoryRetentionRow"));
        _historyContentMotion = new SettingsCollapseMotion(Element<FrameworkElement>("HistoryContent"));
        _navigation = new SettingsNavigationHistory(
            Pages.FirstOrDefault(page => Element<RadioButton>("Nav_" + page).IsChecked == true) ?? Pages[0]);
        model.MusicHistoryChanged += OnMusicHistoryChanged;
        Window.Closed += (_, _) =>
        {
            _statusTimer.Stop();
            scrolling.Dispose();
            navigationIndicator.Dispose();
            _scrollMotion.Dispose();
            _pageTransition.Dispose();
            _dialogMotion.Dispose();
            foreach (var (_, motion) in _dropdowns) motion.Dispose();
            model.MusicHistoryChanged -= OnMusicHistoryChanged;
        };
        Window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        Window.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnNavigationChecked));
        Window.PreviewKeyDown += OnPreviewKeyDown;
        Window.PreviewMouseDown += OnPreviewMouseDown;
        Window.PreviewMouseUp += OnPreviewMouseUp;
        var shortcutInput = Element<Border>("ShortcutInput");
        shortcutInput.PreviewKeyDown += RecordShortcut;
        shortcutInput.MouseLeftButtonDown += (_, _) => shortcutInput.Focus();
        Element<TextBlock>("AppVersion").MouseLeftButtonDown += OnVersionClicked;
        if (model.DeveloperSettingsUnlocked) Element<RadioButton>("Nav_developer").Visibility = Visibility.Visible;

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
        var scanQrCodes = Element<CheckBox>("ScanQrCodes");
        scanQrCodes.Checked += OnScanQrCodesChanged;
        scanQrCodes.Unchecked += OnScanQrCodesChanged;
        var ignoreFullscreen = Element<CheckBox>("IgnoreFullscreen");
        ignoreFullscreen.Checked += OnIgnoreFullscreenChanged;
        ignoreFullscreen.Unchecked += OnIgnoreFullscreenChanged;
        var uiSounds = Element<CheckBox>("UiSounds");
        uiSounds.Checked += OnUiSoundsChanged;
        uiSounds.Unchecked += OnUiSoundsChanged;
        var launch = Element<CheckBox>("Launch");
        launch.Checked += OnLaunchChanged;
        launch.Unchecked += OnLaunchChanged;
        var saveHistory = Element<CheckBox>("SaveHistory");
        saveHistory.Checked += OnSaveHistoryChanged;
        saveHistory.Unchecked += OnSaveHistoryChanged;
        var historyRetention = Element<ComboBox>("HistoryRetention");
        foreach (var days in MusicHistory.RetentionDays)
            historyRetention.Items.Add(new ComboBoxItem { Tag = days });
        historyRetention.Items.Add(new ComboBoxItem { Tag = MusicHistory.KeepForever });
        historyRetention.SelectionChanged += OnHistoryRetentionChanged;
        AddDropdown(historyRetention);

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
        // The audio output and startup entry can change from Windows while this window stays open.
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
        if (e.OriginalSource is not RadioButton { Tag: string page } || !Pages.Contains(page)) return;
        _navigation.Visit(page);
        ShowPage(page);
    }

    // A side press closes an open dropdown, so its release must not navigate as well.
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsSideButton(e)) return;
        _sidePressClosedDropdown = false;
        foreach (var (comboBox, _) in _dropdowns)
        {
            if (!comboBox.IsDropDownOpen) continue;
            comboBox.IsDropDownOpen = false;
            _sidePressClosedDropdown = true;
        }
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsSideButton(e)) return;
        e.Handled = true;
        if (_sidePressClosedDropdown)
        {
            _sidePressClosedDropdown = false;
            return;
        }
        if (IsDialogOpen)
        {
            if (e.ChangedButton == MouseButton.XButton1) CloseDialog();
            return;
        }
        var page = e.ChangedButton == MouseButton.XButton1 ? _navigation.GoBack() : _navigation.GoForward();
        if (page is not null) Element<RadioButton>("Nav_" + page).IsChecked = true;
    }

    private static bool IsSideButton(MouseButtonEventArgs e) =>
        e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2;

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
            case "edit": OpenDialog(SettingsDialog.Shortcut); break;
            case "reset": OpenDialog(SettingsDialog.Reset); break;
            case "clear-history": OpenDialog(SettingsDialog.ClearHistory); break;
            case "provider-menu": OpenDialog(SettingsDialog.ProviderMenu); break;
            case "cancel" or "done": CloseDialog(); break;
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
            case "confirm-clear-history":
                var cleared = _model.ClearMusicHistory();
                _history.Refresh();
                CloseDialog();
                if (!cleared) ShowStatus(_strings.StorageSaveFailed);
                break;
            case "github": _model.Project.OpenRepository(); break;
            case "feedback": _model.Project.OpenFeedback(); break;
            case "license": _model.OpenLicenses(); break;
            case "donate": _model.Project.Open(); break;
            case "folder": _model.OpenDataFolder(); break;
            case "logs": _model.OpenLogsFolder(); break;
            case "ocr-languages": _model.OpenOcrLanguageSettings(); break;
            case "check-updates": _ = CheckForUpdatesAsync((Button)e.OriginalSource); break;
            case "show-onboarding": _model.ShowOnboarding(); break;
            case "test-browser": _model.OpenTestBrowser(); break;
            case "test-notifications": _model.ShowTestNotifications(); break;
        }
        e.Handled = true;
    }

    private async Task CheckForUpdatesAsync(Button button)
    {
        if (!_model.CanCheckForUpdates)
        {
            ShowStatus(_strings.SettingsPreviewText("updates_unavailable"));
            return;
        }
        button.IsEnabled = false;
        ShowStatus(_strings.SettingsPreviewText("updates_checking"));
        try
        {
            ShowStatus(_strings.SettingsPreviewText(await _model.CheckForUpdatesAsync() switch
            {
                UpdateCheckOutcome.UpToDate => "updates_current",
                UpdateCheckOutcome.Offered => "updates_available",
                UpdateCheckOutcome.Installing => "updates_installing",
                _ => "updates_failed",
            }));
        }
        catch (OperationCanceledException) { }
        finally { button.IsEnabled = true; }
    }

    // Like Android's build number: three quick clicks on the version reveal the developer page.
    private void OnVersionClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 3) return;
        _model.UnlockDeveloperSettings();
        Element<RadioButton>("Nav_developer").Visibility = Visibility.Visible;
        ShowStatus(_strings.SettingsPreviewText("developer_unlocked"));
    }

    private void OpenDialog(SettingsDialog dialog)
    {
        var shortcut = dialog == SettingsDialog.Shortcut;
        _scrollMotion.Reset();
        HideStatus();
        if (Element<Border>("DialogLayer").Visibility != Visibility.Visible)
            _dialogOwner = Keyboard.FocusedElement;
        _pendingShortcut = null;
        Element<Grid>("Workspace").IsEnabled = false;
        Element<StackPanel>("ShortcutDialog").Visibility = shortcut ? Visibility.Visible : Visibility.Collapsed;
        Element<StackPanel>("ResetDialog").Visibility = Visible(dialog == SettingsDialog.Reset);
        Element<StackPanel>("ClearHistoryDialog").Visibility = Visible(dialog == SettingsDialog.ClearHistory);
        Element<StackPanel>("ProviderMenuDialog").Visibility = Visible(dialog == SettingsDialog.ProviderMenu);
        if (dialog == SettingsDialog.ProviderMenu) _providerMenu.Refresh();
        Element<Button>("CancelDialog").Visibility = Visible(dialog != SettingsDialog.ProviderMenu);
        Element<Button>("DoneDialog").Visibility = Visible(dialog == SettingsDialog.ProviderMenu);
        Element<Button>("SaveShortcut").Visibility = shortcut ? Visibility.Visible : Visibility.Collapsed;
        Element<Button>("SaveShortcut").IsEnabled = false;
        Element<Button>("ConfirmReset").Visibility = Visible(dialog == SettingsDialog.Reset);
        Element<Button>("ConfirmClearHistory").Visibility = Visible(dialog == SettingsDialog.ClearHistory);
        _dialogMotion.Open();
        ShowShortcutPrompt(_strings.SettingsPreviewText("press_shortcut"));
        if (shortcut) Element<Border>("ShortcutInput").Focus();
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
        if (e.Key != Key.Escape || !IsDialogOpen) return;
        CloseDialog();
        e.Handled = true;
    }

    private bool IsDialogOpen => Element<Border>("DialogLayer").Visibility == Visibility.Visible;

    private void RecordShortcut(object sender, KeyEventArgs e)
    {
        if (!_dialogMotion.IsOpen) return;
        if (e.Key is Key.Tab or Key.Escape || ShortcutText.ClosesWindow(e)) return;
        e.Handled = true;
        if (!e.IsRepeat) _shortcutKeyPressed();
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ShortcutText.IsModifier(key)) return;
        _pendingShortcut = ShortcutText.Gesture(key, e.KeyboardDevice.Modifiers);
        Element<Button>("SaveShortcut").IsEnabled = _pendingShortcut is not null;
        if (_pendingShortcut is null) ShowShortcutPrompt(_strings.SettingsShortcutInvalid);
        else ShowShortcutKeys(_pendingShortcut);
    }

    private void ShowShortcutPrompt(string text)
    {
        var prompt = Element<TextBlock>("ShortcutPrompt");
        prompt.Text = text;
        prompt.Visibility = Visibility.Visible;
        Element<ItemsControl>("ShortcutDraftKeys").ItemsSource = null;
    }

    private void ShowShortcutKeys(string gesture)
    {
        Element<TextBlock>("ShortcutPrompt").Visibility = Visibility.Collapsed;
        Element<ItemsControl>("ShortcutDraftKeys").ItemsSource = ShortcutText.Keys(gesture, _strings);
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            ShowProviderMenuSummary();
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
            Element<CheckBox>("UiSounds").IsChecked = _model.UiSounds;
            Element<CheckBox>("ScanQrCodes").IsChecked = _model.ScanQrCodes;
            Element<CheckBox>("Launch").IsChecked = _model.LaunchAtStartup;
            var cleanup = Element<ComboBox>("Cleanup");
            cleanup.SelectedItem = cleanup.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, _model.BrowserDataCleanupDays));
            foreach (var (name, action) in ToolbarActions)
                Element<CheckBox>(name).IsChecked = _model.IsToolbarActionShown(action);
            Element<CheckBox>("SaveHistory").IsChecked = _model.SaveMusicHistory;
            var historyRetention = Element<ComboBox>("HistoryRetention");
            historyRetention.SelectedItem = historyRetention.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, _model.MusicHistoryRetentionDays));
        }
        finally { _loadingSettings = false; }
        _historyRetentionMotion.Set(_model.SaveMusicHistory);
        _historyContentMotion.Set(_model.SaveMusicHistory);
        _history.RefreshIfOutdated();
        var keys = ShortcutText.Keys(_model.HotkeyGesture, _strings);
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
        foreach (var item in Items("TextSearch"))
            item.Content = _strings.SettingsPreviewText(Equals(item.Tag, TextSearchEngines.MatchImageSearch)
                ? "match_image_search" : "engine_" + item.Tag);
        foreach (var item in Items("Cleanup"))
            item.Content = _strings.SettingsPreviewText(Equals(item.Tag, BrowserDataCleanup.Never) ? "never" : "every_" + item.Tag);
        Items("AppLanguage")[0].Content = _strings.SettingsPreviewText("match_system");
        foreach (var item in Items("HistoryRetention"))
            item.Content = _strings.SettingsPreviewText(Equals(item.Tag, MusicHistory.KeepForever)
                ? "never" : "history_days_" + item.Tag);
        _history.ApplyTexts();
        _providerMenu.ApplyTexts();
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

    private void OnUiSoundsChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (_model.SelectUiSounds(Element<CheckBox>("UiSounds").IsChecked == true)) return;
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnScanQrCodesChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (_model.SelectScanQrCodes(Element<CheckBox>("ScanQrCodes").IsChecked == true)) return;
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

    private void OnSaveHistoryChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var saved = _model.SelectSaveMusicHistory(Element<CheckBox>("SaveHistory").IsChecked == true);
        LoadSettings();
        if (!saved) ShowStatus(_strings.StorageSaveFailed);
    }

    private void OnHistoryRetentionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || Element<ComboBox>("HistoryRetention").SelectedItem is not ComboBoxItem { Tag: int days }) return;
        var saved = _model.SelectMusicHistoryRetention(days);
        LoadSettings();
        if (!saved) ShowStatus(_strings.StorageSaveFailed);
    }

    // Tracks are recorded on a background thread while this window may be open.
    private void OnMusicHistoryChanged()
    {
        if (Window.Dispatcher.CheckAccess()) _history.Refresh();
        else Window.Dispatcher.BeginInvoke(_history.Refresh);
    }

    private void OnToolbarActionChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || sender is not CheckBox { Tag: SelectionToolbarAction action } toggle) return;
        if (_model.ShowToolbarAction(action, toggle.IsChecked == true)) return;
        LoadSettings();
        ShowStatus(_strings.StorageSaveFailed);
    }

    private void ShowProviderMenuSummary() => Element<TextBlock>("ProviderMenuSummary").Text =
        string.Join(" · ", _model.ProvidersShownInMenu.Select(provider => provider.DisplayName));

    private void ShowStatus(string text)
    {
        Element<TextBlock>("StatusText").Text = text;
        _statusMotion.Show();
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void HideStatus()
    {
        _statusTimer.Stop();
        _statusMotion.Hide();
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private enum SettingsDialog { Shortcut, Reset, ClearHistory, ProviderMenu }

    private sealed class PreviewText(UiStrings strings)
    {
        public string this[string key] => strings.SettingsPreviewText(key);
    }
}
