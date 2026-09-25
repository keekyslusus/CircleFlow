using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Missing_file_creates_defaults_and_all_fields_round_trip_with_a_previous_version_backup()
    {
        var (paths, store) = Create();
        var defaults = store.Load();
        Assert.Equal(new AppSettings(), defaults.Settings);
        Assert.False(defaults.Recovered);
        Assert.False(File.Exists(paths.SettingsBackupFilePath));
        var updated = new AppSettings
        {
            SearchProviderId = SearchProviderIds.TraceMoe, TextSearchEngineId = "kagi", HotkeyGesture = "Win+Ctrl+Shift+F7",
            MaxLongSidePx = 8000, PaddingPx = 100, HideDelayMilliseconds = 2000, LassoMinDiagonalPx = 1000,
            OcrLanguageTag = "ru-RU", AppLanguageTag = "ru", ImageTranslationPrivacyConsentAccepted = true, IgnoreHotkeyInFullscreen = false,
            HiddenToolbarActions = SelectionToolbarAction.Ask | SelectionToolbarAction.Save, BrowserDataCleanupDays = 0,
        };
        store.Save(updated);
        Assert.Equal(updated, new SettingsStore(paths).Load().Settings);
        Assert.Equal(defaults.Settings, Read(paths.SettingsBackupFilePath));
        store.Save(updated with { PaddingPx = 9 });
        Assert.Equal(updated, Read(paths.SettingsBackupFilePath));
        Assert.Empty(Directory.GetFiles(paths.DataDirectory, "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Corrupt_main_is_preserved_and_replaced_by_backup_or_defaults_once(bool hasBackup)
    {
        var (paths, store) = Create();
        var expected = new AppSettings();
        if (hasBackup)
        {
            expected = expected with { PaddingPx = 23, ImageTranslationPrivacyConsentAccepted = true };
            File.WriteAllText(paths.SettingsBackupFilePath, JsonSerializer.Serialize(expected));
        }
        const string damaged = "{\"PaddingPx\":12, broken";
        File.WriteAllText(paths.SettingsFilePath, damaged);
        var result = store.Load();
        Assert.True(result.Recovered);
        Assert.Equal(expected, result.Settings);
        Assert.Equal(expected, Read(paths.SettingsFilePath));
        var preserved = Assert.Single(Directory.GetFiles(paths.DataDirectory, "settings.corrupt-*.json"));
        Assert.Equal(damaged, File.ReadAllText(preserved));
        if (hasBackup) Assert.Equal(expected, Read(paths.SettingsBackupFilePath));
        Assert.False(store.Load().Recovered);
        Assert.Single(Directory.GetFiles(paths.DataDirectory, "settings.corrupt-*.json"));
    }

    [Fact]
    public void Invalid_backup_uses_defaults_without_destroying_either_original()
    {
        var (paths, store) = Create();
        File.WriteAllText(paths.SettingsFilePath, "null");
        File.WriteAllText(paths.SettingsBackupFilePath, "invalid backup");
        Assert.Equal(new AppSettings(), store.Load().Settings);
        Assert.Equal("null", File.ReadAllText(Assert.Single(Directory.GetFiles(paths.DataDirectory, "settings.corrupt-*.json"))));
        Assert.Equal("invalid backup", File.ReadAllText(paths.SettingsBackupFilePath));
    }

    [Fact]
    public void Invalid_fields_reset_individually_and_canonical_values_are_written_back()
    {
        var (paths, store) = Create();
        File.WriteAllText(paths.SettingsFilePath, JsonSerializer.Serialize(new AppSettings
        {
            MaxLongSidePx = 1, PaddingPx = -1, HideDelayMilliseconds = 2001, LassoMinDiagonalPx = 0,
            SearchProviderId = "unknown", TextSearchEngineId = "yandex", HotkeyGesture = "Ctrl++A", OcrLanguageTag = "not a language",
            AppLanguageTag = "not a language", ImageTranslationPrivacyConsentAccepted = true, BrowserDataCleanupDays = 7,
        }));
        var result = store.Load();
        Assert.False(result.Recovered);
        Assert.Equal(10, result.ResetFields.Count);
        Assert.Equal(new AppSettings { ImageTranslationPrivacyConsentAccepted = true }, result.Settings);
        Assert.Equal(result.Settings, Read(paths.SettingsFilePath));
        Assert.Empty(store.Load().ResetFields);
    }

    [Fact]
    public void Hidden_toolbar_actions_are_stored_by_name_and_unknown_bits_are_reset()
    {
        var (paths, store) = Create();
        store.Save(new AppSettings { HiddenToolbarActions = SelectionToolbarAction.Copy | SelectionToolbarAction.Translate });
        Assert.Contains("\"HiddenToolbarActions\": \"Copy, Translate\"", File.ReadAllText(paths.SettingsFilePath));

        File.WriteAllText(paths.SettingsFilePath, """{"HiddenToolbarActions":"17"}""");
        var result = store.Load();
        Assert.Equal([nameof(AppSettings.HiddenToolbarActions)], result.ResetFields);
        Assert.Equal(SelectionToolbarAction.Ask, result.Settings.HiddenToolbarActions);
    }

    [Fact]
    public void Invalid_JSON_field_types_reset_only_the_affected_values()
    {
        var (paths, store) = Create();
        var backup = new AppSettings { PaddingPx = 19 };
        File.WriteAllText(paths.SettingsBackupFilePath, JsonSerializer.Serialize(backup));
        File.WriteAllText(paths.SettingsFilePath,
            """{"PaddingPx":"wrong","MaxLongSidePx":999999999999999,"OcrLanguageTag":null,"ImageTranslationPrivacyConsentAccepted":"true","SearchProviderId":"yandex-images","TranslationTargetLanguageTag":"ja-JP","IgnoreHotkeyInFullscreen":"no","HiddenToolbarActions":"Ask, Share"}""");
        var result = store.Load();
        Assert.False(result.Recovered);
        Assert.Equal(6, result.ResetFields.Count);
        Assert.Equal(new AppSettings { SearchProviderId = SearchProviderIds.YandexImages }, result.Settings);
        Assert.True(result.Settings.IgnoreHotkeyInFullscreen);
        Assert.Equal(SelectionToolbarAction.None, result.Settings.HiddenToolbarActions);
        Assert.Equal(result.Settings, Read(paths.SettingsFilePath));
        Assert.DoesNotContain("TranslationTargetLanguageTag", File.ReadAllText(paths.SettingsFilePath));
        Assert.Equal(backup, Read(paths.SettingsBackupFilePath));
    }

    [Fact]
    public void Replace_failure_preserves_main_and_backup_and_removes_temporary_file()
    {
        var (paths, store) = Create();
        store.Load();
        store.Save(new AppSettings { PaddingPx = 20 });
        var previousMain = File.ReadAllBytes(paths.SettingsFilePath);
        var previousBackup = File.ReadAllBytes(paths.SettingsBackupFilePath);
        using (var locked = new FileStream(paths.SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() => store.Save(new AppSettings { PaddingPx = 30 }));
        Assert.Equal(previousMain, File.ReadAllBytes(paths.SettingsFilePath));
        Assert.Equal(previousBackup, File.ReadAllBytes(paths.SettingsBackupFilePath));
        Assert.Empty(Directory.GetFiles(paths.DataDirectory, "*.tmp"));
    }

    [Fact]
    public void Failure_to_preserve_corrupt_file_never_overwrites_it()
    {
        var (paths, store) = Create();
        File.WriteAllText(paths.SettingsFilePath, "damaged original");
        File.WriteAllText(paths.SettingsBackupFilePath, JsonSerializer.Serialize(new AppSettings { PaddingPx = 40 }));
        var directory = new DirectoryInfo(paths.DataDirectory);
        var originalAcl = directory.GetAccessControl();
        var deniedAcl = directory.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        deniedAcl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(deniedAcl);
            Assert.Throws<UnauthorizedAccessException>(() => store.Load());
        }
        finally { directory.SetAccessControl(originalAcl); }
        Assert.Equal("damaged original", File.ReadAllText(paths.SettingsFilePath));
        Assert.Equal(40, Read(paths.SettingsBackupFilePath).PaddingPx);
        Assert.Empty(Directory.GetFiles(paths.DataDirectory, "settings.corrupt-*.json"));
    }

    [Fact]
    public async Task Parallel_provider_consent_and_general_patches_are_all_saved()
    {
        var (paths, store) = Create();
        var service = TestSettings.Create(store.Load().Settings, store.Save);
        var results = await Task.WhenAll(
            Task.Run(() => service.SetProvider(SearchProviderIds.YandexImages)),
            Task.Run(() => service.SetTranslationConsent(true)),
            Task.Run(() => service.Apply(new SettingsEdits { PaddingPx = 29 })));
        Assert.All(results, result => Assert.True(result.Success));
        var expected = new AppSettings { SearchProviderId = SearchProviderIds.YandexImages, ImageTranslationPrivacyConsentAccepted = true, PaddingPx = 29 };
        Assert.Equal(expected, service.Snapshot);
        Assert.Equal(expected, Read(paths.SettingsFilePath));
    }

    [Fact]
    public void Missing_Data_fails_without_a_fallback_location()
    {
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "missing-data-" + Guid.NewGuid().ToString("N")));
        Assert.Throws<DirectoryNotFoundException>(() => new SettingsStore(paths).Load());
        Assert.False(Directory.Exists(paths.RootDirectory));
    }

    private static AppSettings Read(string path) => JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))!;

    private static (AppPaths Paths, SettingsStore Store) Create()
    {
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "настройки " + Guid.NewGuid().ToString("N")));
        AppDataDirectory.Initialize(paths);
        return (paths, new SettingsStore(paths));
    }
}
