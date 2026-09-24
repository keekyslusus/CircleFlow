using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsWindowModel(
    SettingsService settings,
    ProviderSelectionStore providers,
    OcrLanguageCatalog ocrLanguages,
    ProjectSupport project,
    UrlOpeningService urlOpening,
    AppPaths paths,
    UiStrings strings,
    Func<string?> webViewRuntimeVersion)
{
    private const string OcrLanguageSettingsUri = "ms-settings:regionlanguage";

    public IReadOnlyList<SearchProviderDescriptor> Providers => providers.Providers;
    public string ProviderId => providers.GetEffectiveSelection().Id;
    public string TextSearchEngineId => settings.Snapshot.TextSearchEngineId;
    public IReadOnlyList<OcrLanguageOption> OcrLanguages => ocrLanguages.AvailableLanguages;
    // Languages can be installed from Windows Settings while this window is open.
    public bool RefreshOcrLanguages() => ocrLanguages.Refresh();
    // A saved language whose pack was removed falls back to the keyboard layout at runtime, so show that.
    public string OcrLanguageTag => ocrLanguages.Resolve(settings.Snapshot.OcrLanguageTag)?.Tag ?? string.Empty;
    public string HotkeyGesture => settings.Snapshot.HotkeyGesture;
    public ProjectSupport Project => project;
    public string? WebViewRuntimeVersion => webViewRuntimeVersion();

    public bool SelectProvider(string providerId) => providers.Save(providerId);

    public bool SelectTextSearchEngine(string engineId) => settings.SetTextSearchEngine(engineId).Success;

    public bool SelectOcrLanguage(string languageTag) =>
        settings.Apply(new SettingsEdits { OcrLanguageTag = languageTag }).Success;

    public string ChangeHotkey(string gesture) =>
        Describe(settings.ChangeHotkey(gesture), gesture, strings.SettingsShortcutSaved);

    // Null means ProviderSelectionStore has already reported the failure.
    public string? ResetToDefaults()
    {
        var defaults = new AppSettings();
        if (!providers.Save(defaults.SearchProviderId)) return null;
        if (!SelectTextSearchEngine(defaults.TextSearchEngineId)) return strings.StorageSaveFailed;
        if (!SelectOcrLanguage(defaults.OcrLanguageTag)) return strings.StorageSaveFailed;
        return Describe(settings.ChangeHotkey(defaults.HotkeyGesture), defaults.HotkeyGesture, strings.SettingsResetDone);
    }

    public void OpenDataFolder() => urlOpening.TryOpen(paths.DataDirectory, strings.SettingsOpenFolderFailed);

    public void OpenLogsFolder() => urlOpening.TryOpen(paths.LogsDirectory, strings.SettingsOpenFolderFailed);

    public void OpenOcrLanguageSettings() =>
        urlOpening.TryOpen(OcrLanguageSettingsUri, strings.SettingsOpenLanguageSettingsFailed);

    private string Describe(SettingsChangeResult result, string gesture, string success) => result.Status switch
    {
        SettingsChangeStatus.Success => success,
        SettingsChangeStatus.Invalid => strings.SettingsShortcutInvalid,
        SettingsChangeStatus.HotkeyUnavailable => strings.SettingsShortcutUnavailable(gesture),
        SettingsChangeStatus.HotkeyRollbackFailed => strings.HotkeyRollbackFailed,
        _ => strings.StorageSaveFailed,
    };
}
