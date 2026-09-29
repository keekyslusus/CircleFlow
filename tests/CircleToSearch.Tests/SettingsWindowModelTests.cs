using System.Windows.Input;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Settings;
using CircleToSearch.Search;
using CircleToSearch.Shell;
using CircleToSearch.Trigger;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SettingsWindowModelTests
{
    [Theory]
    [InlineData(Key.K, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+K")]
    [InlineData(Key.D7, ModifierKeys.Shift | ModifierKeys.Control, "Ctrl+Shift+7")]
    [InlineData(Key.Space, ModifierKeys.Alt, "Alt+Space")]
    [InlineData(Key.K, ModifierKeys.None, null)]
    [InlineData(Key.K, ModifierKeys.Windows | ModifierKeys.Control, null)]
    [InlineData(Key.F5, ModifierKeys.Control, null)]
    public void Recorded_shortcuts_use_the_gesture_format_the_hotkey_parser_accepts(
        Key key, ModifierKeys modifiers, string? expected)
    {
        var gesture = ShortcutText.Gesture(key, modifiers);

        Assert.Equal(expected, gesture);
        if (gesture is not null) Assert.True(HotkeyGestureParser.TryParse(gesture, out _, out _));
    }

    [Fact]
    public void A_busy_history_file_does_not_fail_retention_changes_or_stop_a_reset()
    {
        var harness = new TestSettingsWindow(TestSettings.Create(new AppSettings { HotkeyGesture = "Ctrl+Shift+K" }));
        // Older than every retention, so each change has to rewrite the file.
        var now = harness.Time.Now;
        harness.Time.Now = now.AddDays(-100);
        harness.History.Record(new ShazamRecognition("Kids", "MGMT", null, null, null, null, null));
        harness.Time.Now = now;

        using (new FileStream(harness.HistoryFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.True(harness.Model.SelectMusicHistoryRetention(7));
            Assert.Equal(7, harness.Settings.Snapshot.MusicHistoryRetentionDays);
            Assert.Equal(harness.Strings.SettingsResetDone, harness.Model.ResetToDefaults());
        }

        Assert.Equal(new AppSettings().MusicHistoryRetentionDays, harness.Settings.Snapshot.MusicHistoryRetentionDays);
        Assert.Equal(new AppSettings().HotkeyGesture, harness.Settings.Snapshot.HotkeyGesture);
    }

    [Fact]
    public void Every_text_search_engine_has_a_localized_name() =>
        Assert.All(TextSearchEngines.All, engine =>
            Assert.True(TestUiStrings.EnglishValues.ContainsKey("app_settings_engine_" + engine.Id), engine.Id));

    [Fact]
    public void Shortcut_conflicts_are_reported_and_keep_the_previous_shortcut()
    {
        var settings = TestSettings.Create(applyHotkey: gesture => gesture == "Ctrl+Alt+K"
            ? new(false, new("Ctrl+Alt+Space", true))
            : new(true, new(gesture ?? string.Empty, gesture is not null)));
        settings.InitializeHotkey();
        var model = new TestSettingsWindow(settings).Model;

        Assert.Equal(TestUiStrings.English.SettingsShortcutUnavailable("Ctrl+Alt+K"), model.ChangeHotkey("Ctrl+Alt+K"));
        Assert.Equal("Ctrl+Alt+Space", model.HotkeyGesture);
        Assert.Equal(TestUiStrings.English.SettingsShortcutSaved, model.ChangeHotkey("Ctrl+Shift+1"));
        Assert.Equal("Ctrl+Shift+1", model.HotkeyGesture);
    }

    [Fact]
    public void Shortcut_save_failure_is_reported()
    {
        var harness = new TestSettingsWindow(TestSettings.Create(save: _ => throw new IOException("disk unavailable")));

        Assert.Equal(TestUiStrings.English.StorageSaveFailed, harness.Model.ChangeHotkey("Ctrl+Shift+1"));
        Assert.Equal("Ctrl+Alt+Space", harness.Model.HotkeyGesture);
    }

    [Fact]
    public void Folder_that_cannot_be_opened_reports_a_folder_error()
    {
        var harness = new TestSettingsWindow(openSucceeds: false);

        harness.Model.OpenLogsFolder();

        Assert.Equal([harness.Paths.LogsDirectory], harness.Opened);
        Assert.Equal([TestUiStrings.English.SettingsOpenFolderFailed], harness.Notifier.Errors.Select(error => error.Message));
    }
}
