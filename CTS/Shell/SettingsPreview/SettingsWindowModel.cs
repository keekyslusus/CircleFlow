using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsWindowModel(
    SettingsService settings,
    ProviderSelectionStore providers,
    ProjectSupport project,
    UrlOpeningService urlOpening,
    AppPaths paths,
    UiStrings strings,
    Func<string?> webViewRuntimeVersion)
{
    public IReadOnlyList<SearchProviderDescriptor> Providers => providers.Providers;
    public string ProviderId => providers.GetEffectiveSelection().Id;
    public string TextSearchEngineId => settings.Snapshot.TextSearchEngineId;
    public string HotkeyGesture => settings.Snapshot.HotkeyGesture;
    public ProjectSupport Project => project;
    public string? WebViewRuntimeVersion => webViewRuntimeVersion();

    public bool SelectProvider(string providerId) => providers.Save(providerId);

    public bool SelectTextSearchEngine(string engineId) => settings.SetTextSearchEngine(engineId).Success;

    public string ChangeHotkey(string gesture) =>
        Describe(settings.ChangeHotkey(gesture), gesture, strings.SettingsShortcutSaved);

    // Null means ProviderSelectionStore has already reported the failure.
    public string? ResetToDefaults()
    {
        var defaults = new AppSettings();
        if (!providers.Save(defaults.SearchProviderId)) return null;
        if (!SelectTextSearchEngine(defaults.TextSearchEngineId)) return strings.StorageSaveFailed;
        return Describe(settings.ChangeHotkey(defaults.HotkeyGesture), defaults.HotkeyGesture, strings.SettingsResetDone);
    }

    public void OpenDataFolder() => urlOpening.TryOpen(paths.DataDirectory, strings.SettingsOpenFolderFailed);

    public void OpenLogsFolder() => urlOpening.TryOpen(paths.LogsDirectory, strings.SettingsOpenFolderFailed);

    private string Describe(SettingsChangeResult result, string gesture, string success) => result.Status switch
    {
        SettingsChangeStatus.Success => success,
        SettingsChangeStatus.Invalid => strings.SettingsShortcutInvalid,
        SettingsChangeStatus.HotkeyUnavailable => strings.SettingsShortcutUnavailable(gesture),
        SettingsChangeStatus.HotkeyRollbackFailed => strings.HotkeyRollbackFailed,
        _ => strings.StorageSaveFailed,
    };
}
