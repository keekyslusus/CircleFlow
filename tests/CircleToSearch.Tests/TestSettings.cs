using CircleToSearch.Settings;
using CircleToSearch.Trigger;

namespace CircleToSearch.Tests;

internal static class TestSettings
{
    public static SettingsService Create(AppSettings? initial = null, Action<AppSettings>? save = null,
        Func<string?, HotkeyApplyResult>? applyHotkey = null)
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "settings-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new SettingsService(initial ?? new AppSettings(), save ?? (_ => { }),
            applyHotkey ?? (gesture => new(true, new(gesture ?? string.Empty, gesture is not null))),
            new PluginLog(directory));
    }
}
