using CircleToSearch.Trigger;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class HotkeyPauseTests
{
    [Fact]
    public void Stays_paused_until_every_window_stops_recording()
    {
        var pause = new HotkeyPause();
        var settings = pause.CreateSwitch();
        var onboarding = pause.CreateSwitch();

        settings.Set(true);
        settings.Set(true);
        onboarding.Set(true);
        Assert.True(pause.IsActive);

        settings.Set(false);
        Assert.True(pause.IsActive);

        onboarding.Set(false);
        onboarding.Set(false);
        Assert.False(pause.IsActive);
    }

    [Fact]
    public void Guarded_hotkey_does_nothing_while_a_shortcut_is_recorded()
    {
        var pause = new HotkeyPause();
        var recording = pause.CreateSwitch();
        var presses = 0;
        var pressed = pause.Guard(() => presses++, new PluginLog(TestOutputPaths.TempDirectory));

        recording.Set(true);
        pressed();
        Assert.Equal(0, presses);

        recording.Set(false);
        pressed();
        Assert.Equal(1, presses);
    }
}
