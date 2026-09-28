using System.Globalization;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;
using CircleToSearch.Ui;
using CircleToSearch.Updates;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsWindowModel(
    SettingsService settings,
    ProviderSelectionStore providers,
    OcrLanguageCatalog ocrLanguages,
    UiLanguage language,
    CultureInfo culture,
    ProjectSupport project,
    UrlOpeningService urlOpening,
    AppPaths paths,
    UiStrings strings,
    Func<string?> webViewRuntimeVersion,
    Func<string?> audioOutputName,
    WindowsStartupRegistration startup,
    Action showOnboarding,
    Action openTestBrowser,
    Func<Task<UpdateCheckOutcome>>? checkForUpdates)
{
    private const string OcrLanguageSettingsUri = "ms-settings:regionlanguage";

    public IReadOnlyList<SearchProviderDescriptor> Providers => providers.Providers;
    public string ProviderId => providers.GetEffectiveSelection().Id;
    public string TextSearchEngineId => settings.Snapshot.TextSearchEngineId;
    public bool TextSearchInBuiltInBrowser => settings.Snapshot.TextSearchInBuiltInBrowser;
    public IReadOnlyList<OcrLanguageOption> OcrLanguages => ocrLanguages.AvailableLanguages;
    // Languages can be installed from Windows Settings while this window is open.
    public bool RefreshOcrLanguages() => ocrLanguages.Refresh();
    // A saved language whose pack was removed falls back to the keyboard layout at runtime, so show that.
    public string OcrLanguageTag => ocrLanguages.Resolve(settings.Snapshot.OcrLanguageTag)?.Tag ?? string.Empty;
    public string TranslationLanguageName => TranslationTargetLanguage.DisplayName(culture);
    public IReadOnlyList<AppLanguageOption> AppLanguages => language.Catalog.Available;
    public string AppLanguageTag => language.Catalog.Find(settings.Snapshot.AppLanguageTag);
    public string HotkeyGesture => settings.Snapshot.HotkeyGesture;
    public bool IgnoreHotkeyInFullscreen => settings.Snapshot.IgnoreHotkeyInFullscreen;
    public int BrowserDataCleanupDays => settings.Snapshot.BrowserDataCleanupDays;
    public bool LaunchAtStartup => startup.IsEnabled;
    public ProjectSupport Project => project;
    public string? WebViewRuntimeVersion => webViewRuntimeVersion();
    public string? AudioOutputName => audioOutputName();
    // Kept for this run only, so developer tools never stay visible after a restart.
    public bool DeveloperSettingsUnlocked { get; private set; }

    public void UnlockDeveloperSettings() => DeveloperSettingsUnlocked = true;

    public void ShowOnboarding() => showOnboarding();

    public void OpenTestBrowser() => openTestBrowser();

    // False for a copy without Update.exe, such as the development build.
    public bool CanCheckForUpdates => checkForUpdates is not null;

    public Task<UpdateCheckOutcome> CheckForUpdatesAsync() =>
        (checkForUpdates ?? throw new InvalidOperationException("This copy cannot check for updates."))();

    public bool SelectProvider(string providerId) => providers.Save(providerId);

    public bool SelectTextSearchEngine(string engineId) => settings.SetTextSearchEngine(engineId).Success;

    public bool SelectTextSearchInBuiltInBrowser(bool builtIn) =>
        settings.SetTextSearchInBuiltInBrowser(builtIn).Success;

    public bool SelectOcrLanguage(string languageTag) =>
        settings.Apply(new SettingsEdits { OcrLanguageTag = languageTag }).Success;

    public bool SelectAppLanguage(string languageTag)
    {
        if (!settings.SetAppLanguage(languageTag).Success) return false;
        language.Apply(settings.Snapshot.AppLanguageTag);
        return true;
    }

    public bool SelectIgnoreHotkeyInFullscreen(bool ignore) => settings.SetIgnoreHotkeyInFullscreen(ignore).Success;

    public bool SelectBrowserDataCleanup(int days) => settings.SetBrowserDataCleanupDays(days).Success;

    public bool SelectLaunchAtStartup(bool enabled) => startup.TrySet(enabled);

    public bool IsToolbarActionShown(SelectionToolbarAction action) =>
        !settings.Snapshot.HiddenToolbarActions.HasFlag(action);

    public bool ShowToolbarAction(SelectionToolbarAction action, bool shown)
    {
        var hidden = settings.Snapshot.HiddenToolbarActions;
        return settings.SetHiddenToolbarActions(shown ? hidden & ~action : hidden | action).Success;
    }

    public string ChangeHotkey(string gesture) =>
        ShortcutText.ChangeMessage(settings.ChangeHotkey(gesture), gesture, strings.SettingsShortcutSaved, strings);

    // Null means ProviderSelectionStore has already reported the failure.
    public string? ResetToDefaults()
    {
        var defaults = new AppSettings();
        if (!providers.Save(defaults.SearchProviderId)) return null;
        if (!SelectTextSearchEngine(defaults.TextSearchEngineId)) return strings.StorageSaveFailed;
        if (!SelectTextSearchInBuiltInBrowser(defaults.TextSearchInBuiltInBrowser)) return strings.StorageSaveFailed;
        if (!SelectOcrLanguage(defaults.OcrLanguageTag)) return strings.StorageSaveFailed;
        if (!SelectAppLanguage(defaults.AppLanguageTag)) return strings.StorageSaveFailed;
        if (!SelectIgnoreHotkeyInFullscreen(defaults.IgnoreHotkeyInFullscreen)) return strings.StorageSaveFailed;
        if (!settings.SetHiddenToolbarActions(defaults.HiddenToolbarActions).Success) return strings.StorageSaveFailed;
        if (!SelectBrowserDataCleanup(defaults.BrowserDataCleanupDays)) return strings.StorageSaveFailed;
        return ShortcutText.ChangeMessage(settings.ChangeHotkey(defaults.HotkeyGesture), defaults.HotkeyGesture,
            strings.SettingsResetDone, strings);
    }

    public void OpenDataFolder() => urlOpening.TryOpen(paths.DataDirectory, strings.SettingsOpenFolderFailed);

    public void OpenLogsFolder() => urlOpening.TryOpen(paths.LogsDirectory, strings.SettingsOpenFolderFailed);

    public void OpenOcrLanguageSettings() =>
        urlOpening.TryOpen(OcrLanguageSettingsUri, strings.SettingsOpenLanguageSettingsFailed);
}
