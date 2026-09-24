using System.Drawing;
using CircleToSearch.Trigger;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class FullscreenAppDetectorTests
{
    private static readonly Rectangle Monitor = new(1920, 0, 2560, 1440);

    [Fact]
    public void A_window_covering_the_whole_monitor_blocks_the_hotkey() =>
        Assert.True(FullscreenAppDetector.BlocksHotkey(Window("UnityWndClass", "Game")));

    [Theory]
    [InlineData("chrome")]
    [InlineData("MSEDGE")]
    [InlineData("firefox")]
    [InlineData("browser")]
    [InlineData("vlc")]
    [InlineData("mpv")]
    [InlineData("PotPlayerMini64")]
    public void Fullscreen_browsers_and_video_players_keep_the_hotkey(string process) =>
        Assert.False(FullscreenAppDetector.BlocksHotkey(Window("AnyClass", process)));

    [Fact]
    public void Shell_console_and_own_windows_keep_the_hotkey()
    {
        Assert.False(FullscreenAppDetector.BlocksHotkey(Window("XamlExplorerHostIslandWindow", "explorer")));
        Assert.False(FullscreenAppDetector.BlocksHotkey(Window("ConsoleWindowClass", "cmd")));
        Assert.False(FullscreenAppDetector.BlocksHotkey(Window("HwndWrapper", "CircleFlow") with { IsOwnProcess = true }));
    }

    [Fact]
    public void Maximized_and_smaller_windows_keep_the_hotkey()
    {
        var maximized = Rectangle.FromLTRB(Monitor.Left - 8, Monitor.Top - 8, Monitor.Right + 8, Monitor.Bottom + 8);
        Assert.False(FullscreenAppDetector.BlocksHotkey(Window("Notepad", "notepad") with { Bounds = maximized }));
        Assert.False(FullscreenAppDetector.BlocksHotkey(Window("Game", "game") with { Bounds = new(1920, 0, 1280, 720) }));
    }

    private static ForegroundWindow Window(string className, string process) =>
        new(className, Monitor, Monitor, process, IsOwnProcess: false);
}
