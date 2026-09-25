using CircleToSearch.Search;
using CircleToSearch.Settings;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Invalid_edits_have_no_partial_commit_and_snapshots_remain_immutable()
    {
        var saves = new List<AppSettings>();
        var service = TestSettings.Create(save: saves.Add);
        var before = service.Snapshot;
        var invalid = service.Apply(new SettingsEdits { PaddingPx = 20, MaxLongSidePx = 255 });
        Assert.Equal(SettingsChangeStatus.Invalid, invalid.Status);
        Assert.Contains(nameof(AppSettings.MaxLongSidePx), invalid.InvalidFields);
        Assert.Same(before, service.Snapshot);
        Assert.Empty(saves);
        Assert.False(service.SetProvider("unknown").Success);
        Assert.False(service.Apply(new SettingsEdits { OcrLanguageTag = "not a language" }).Success);
        Assert.True(service.Apply(new SettingsEdits { PaddingPx = 20, OcrLanguageTag = " en-us " }).Success);
        Assert.Equal(8, before.PaddingPx);
        Assert.Equal(20, service.Snapshot.PaddingPx);
        Assert.Equal("en-US", service.Snapshot.OcrLanguageTag);
        Assert.False(service.SetAppLanguage("not a language").Success);
        Assert.True(service.SetAppLanguage(" RU ").Success);
        Assert.Equal("ru", service.Snapshot.AppLanguageTag);
        var editedCopy = service.Snapshot with { PaddingPx = 99 };
        Assert.Equal(20, service.Snapshot.PaddingPx);
        Assert.Equal(99, editedCopy.PaddingPx);
    }

    [Fact]
    public void Failed_save_preserves_provider_consent_and_general_settings()
    {
        var service = TestSettings.Create(save: _ => throw new IOException("disk failure"));
        Assert.Equal(SettingsChangeStatus.SaveFailed, service.SetProvider(SearchProviderIds.YandexImages).Status);
        Assert.Equal(SettingsChangeStatus.SaveFailed, service.SetTranslationConsent(true).Status);
        Assert.Equal(SettingsChangeStatus.SaveFailed, service.Apply(new SettingsEdits { PaddingPx = 14 }).Status);
        Assert.Equal(new AppSettings(), service.Snapshot);
    }

    [Fact]
    public void Completed_onboarding_is_saved_and_a_failed_save_keeps_it_pending()
    {
        var saves = new List<AppSettings>();
        var service = TestSettings.Create(save: saves.Add);
        Assert.True(service.CompleteOnboarding().Success);
        Assert.True(service.Snapshot.OnboardingCompleted);
        Assert.True(Assert.Single(saves).OnboardingCompleted);

        var failing = TestSettings.Create(save: _ => throw new IOException("disk failure"));
        Assert.Equal(SettingsChangeStatus.SaveFailed, failing.CompleteOnboarding().Status);
        Assert.False(failing.Snapshot.OnboardingCompleted);
    }

    [Fact]
    public void Hotkey_is_registered_before_save_and_committed_only_after_save()
    {
        var native = new TestHotkeyRegistration();
        SettingsService? service = null;
        AppSettings? saved = null;
        service = TestSettings.Create(save: settings =>
        {
            Assert.Equal("Ctrl+F7", native.Current);
            Assert.Equal("Ctrl+Alt+Space", service!.Snapshot.HotkeyGesture);
            saved = settings;
        }, applyHotkey: native.Registrar.TryApply);
        Assert.True(service.InitializeHotkey().Success);
        Assert.True(service.ChangeHotkey("ctrl + f7").Success);
        Assert.Equal("Ctrl+F7", service.Snapshot.HotkeyGesture);
        Assert.Equal(service.Snapshot, saved);
        Assert.True(service.HotkeyStatus.IsActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registration_conflict_preserves_settings_and_reports_rollback_status(bool rollbackSucceeds)
    {
        var native = new TestHotkeyRegistration();
        var saves = 0;
        var service = TestSettings.Create(save: _ => saves++, applyHotkey: native.Registrar.TryApply);
        service.InitializeHotkey();
        native.Results.Enqueue(false);
        native.Results.Enqueue(rollbackSucceeds);
        var result = service.ChangeHotkey("Ctrl+F7");
        Assert.Equal(rollbackSucceeds ? SettingsChangeStatus.HotkeyUnavailable : SettingsChangeStatus.HotkeyRollbackFailed, result.Status);
        Assert.Equal("Ctrl+Alt+Space", service.Snapshot.HotkeyGesture);
        Assert.Equal(rollbackSucceeds, result.Hotkey.IsActive);
        Assert.Equal(rollbackSucceeds ? "Ctrl+Alt+Space" : null, native.Current);
        Assert.Equal(0, saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_failure_restores_registration_and_exposes_failed_rollback(bool rollbackSucceeds)
    {
        var native = new TestHotkeyRegistration();
        var service = TestSettings.Create(save: _ => throw new IOException("disk"), applyHotkey: native.Registrar.TryApply);
        service.InitializeHotkey();
        native.Results.Enqueue(true);
        native.Results.Enqueue(rollbackSucceeds);
        if (!rollbackSucceeds) native.Results.Enqueue(true);
        var result = service.ChangeHotkey("Ctrl+F7");
        Assert.Equal(rollbackSucceeds ? SettingsChangeStatus.SaveFailed : SettingsChangeStatus.HotkeyRollbackFailed, result.Status);
        Assert.Equal("Ctrl+Alt+Space", service.Snapshot.HotkeyGesture);
        Assert.Equal(rollbackSucceeds ? "Ctrl+Alt+Space" : "Ctrl+F7", native.Current);
        Assert.Equal(native.Current, result.Hotkey.Gesture);
        Assert.True(result.Hotkey.IsActive);
    }

    [Fact]
    public void Save_failure_restores_an_inactive_state_without_registering_the_old_setting()
    {
        var native = new TestHotkeyRegistration();
        var service = TestSettings.Create(save: _ => throw new IOException("disk"), applyHotkey: native.Registrar.TryApply);
        var result = service.ChangeHotkey("Ctrl+F7");
        Assert.Equal(SettingsChangeStatus.SaveFailed, result.Status);
        Assert.False(result.Hotkey.IsActive);
        Assert.Null(native.Current);
        Assert.Equal(new[] { "Ctrl+F7" }, native.Attempts);
    }

    [Fact]
    public void Invalid_hotkey_never_releases_the_active_registration_and_general_edits_do_not_touch_it()
    {
        var native = new TestHotkeyRegistration();
        var service = TestSettings.Create(applyHotkey: native.Registrar.TryApply);
        service.InitializeHotkey();
        var result = service.ChangeHotkey("Ctrl++Space");
        Assert.Equal(SettingsChangeStatus.Invalid, result.Status);
        Assert.True(result.Hotkey.IsActive);
        Assert.True(service.Apply(new SettingsEdits { PaddingPx = 14 }).Success);
        Assert.True(service.ChangeHotkey("Ctrl+Alt+Space").Success);
        Assert.Single(native.Attempts);
        Assert.Equal(0, native.UnregisterCalls);
    }
}
