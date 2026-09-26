using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CircleToSearch.Interop;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class BottomResultsPanelTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 500)]
    [InlineData(false, 500)]
    public async Task Native_window_animates_even_after_slow_creation_and_finishes_above_taskbar(
        bool animationsEnabled, int creationDelay)
    {
        using var dispatcher = new StaDispatcher("Bottom sheet test");
        Window? window = null;
        BottomResultsPanel? panel = null;
        var positions = new List<int>();
        EventHandler sample = (_, _) =>
        {
            if (window is not null && GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds))
                positions.Add(bounds.Top);
        };
        NativeMethods.GetCursorPos(out var anchor);
        var monitor = NativeMethods.MonitorFromPoint(anchor, 2);
        var info = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        Assert.True(NativeMethods.GetMonitorInfoW(monitor, ref info));
        try
        {
            dispatcher.Send(() =>
            {
                window = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowActivated = false, ShowInTaskbar = false };
                panel = new BottomResultsPanel(window, anchor, animationsEnabled);
                // Reproduce expensive startup work before the dispatcher can present its first frame.
                window.SourceInitialized += (_, _) => Thread.Sleep(creationDelay);
                _ = panel.ShowAsync();
                var hwnd = new WindowInteropHelper(window).Handle;
                Assert.Equal(0x00C00000, NativeMethods.GetWindowLongW(hwnd, -16) & 0x00C00000);
                CompositionTarget.Rendering += sample;
            });
            await panel!.EntranceCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.Send(() =>
            {
                Assert.True(GetWindowRect(new WindowInteropHelper(window!).Handle, out var bounds));
                Assert.True(bounds.Left >= info.Work.Left);
                Assert.True(bounds.Right <= info.Work.Right);
                Assert.True(bounds.Top >= info.Work.Top);
                Assert.InRange(bounds.Bottom, info.Work.Bottom - 50, info.Monitor.Bottom - 1);
                Assert.True(bounds.Bottom - bounds.Top > 100);
                if (animationsEnabled)
                {
                    Assert.True(positions.Distinct().Count() >= 4, string.Join(", ", positions));
                    Assert.Contains(positions, top => top > bounds.Top + 20 && top < bounds.Bottom - 20);
                    Assert.Equal(positions.OrderDescending(), positions);
                }
                else Assert.All(positions, top => Assert.Equal(bounds.Top, top));
            });

            dispatcher.Send(() =>
            {
                positions.Clear();
                panel.MoveTo(anchor);
                _ = panel.ShowAsync();
            });
            await panel.EntranceCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.Send(() =>
            {
                Assert.True(GetWindowRect(new WindowInteropHelper(window!).Handle, out var bounds));
                if (animationsEnabled)
                {
                    Assert.True(positions.Distinct().Count() >= 4, string.Join(", ", positions));
                    Assert.Contains(positions, top => top > bounds.Top + 20 && top < bounds.Bottom - 20);
                    Assert.Equal(positions.OrderDescending(), positions);
                }
                Assert.InRange(bounds.Bottom, info.Work.Bottom - 50, info.Monitor.Bottom - 1);
            });
        }
        finally
        {
            dispatcher.Send(() =>
            {
                CompositionTarget.Rendering -= sample;
                window?.Close();
            });
        }
    }

    [Fact]
    public async Task Closing_before_first_frame_completes_entrance_wait()
    {
        using var dispatcher = new StaDispatcher("Bottom sheet close test");
        Task? entrance = null;
        dispatcher.Send(() =>
        {
            var window = new Window { ShowActivated = false, ShowInTaskbar = false };
            var panel = new BottomResultsPanel(window, new POINT(), true);
            entrance = panel.EntranceCompleted;
            _ = panel.ShowAsync();
            window.Close();
        });
        await entrance!.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Hidden_sheet_is_cloaked_without_a_region_until_it_is_revealed()
    {
        using var dispatcher = new StaDispatcher("Bottom sheet cloak test");
        Window? window = null;
        BottomResultsPanel? panel = null;
        NativeMethods.GetCursorPos(out var anchor);
        try
        {
            dispatcher.Send(() =>
            {
                window = new Window { Content = new System.Windows.Controls.Grid(), ShowInTaskbar = false };
                panel = new BottomResultsPanel(window, anchor, animationsEnabled: true);
                panel.ShowHidden();
                var hwnd = new WindowInteropHelper(window).Handle;
                Assert.True(window.IsVisible);
                Assert.True(IsCloaked(hwnd));
                Assert.False(HasRegion(hwnd));
                panel.MoveTo(anchor);
                _ = panel.ShowAsync();
            });
            await panel!.EntranceCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.Send(() =>
            {
                var hwnd = new WindowInteropHelper(window!).Handle;
                Assert.False(IsCloaked(hwnd));
                Assert.False(HasRegion(hwnd));
            });
        }
        finally
        {
            dispatcher.Send(() => window?.Close());
        }
    }

    [Fact]
    public void Entrance_rises_from_the_screen_edge_unless_a_monitor_lies_below()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var target = BottomResultsPanel.CalculateBounds(monitor, new Rectangle(0, 0, 1920, 1032), 1, false);

        Assert.Equal(1080, BottomResultsPanel.CalculateEntranceTop(monitor, target, monitorBelow: false));
        Assert.Equal(target.Top + 1080 - target.Bottom,
            BottomResultsPanel.CalculateEntranceTop(monitor, target, monitorBelow: true));
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    // GetWindowRgn reports ERROR (0) for a window that has no region.
    private static bool HasRegion(IntPtr hwnd)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try { return GetWindowRgn(hwnd, region) != 0; }
        finally { NativeMethods.DeleteObject(region); }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT bounds);

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Sheet_stays_above_taskbar_with_scaled_gap(double scale)
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var work = new Rectangle(0, 0, 1920, 1032);
        var result = BottomResultsPanel.CalculateBounds(monitor, work, scale, false);
        Assert.Equal(work.Bottom - (int)Math.Round(12 * scale), result.Bottom);
        Assert.True(work.Contains(result));
        Assert.Equal(work.Left + (work.Width - result.Width) / 2, result.Left);
    }

    [Fact]
    public void Autohide_visibility_does_not_move_or_resize_sheet()
    {
        var monitor = new Rectangle(-1920, -1080, 1920, 1080);
        var hidden = BottomResultsPanel.CalculateBounds(monitor, monitor, 1.5, true);
        var shown = BottomResultsPanel.CalculateBounds(monitor,
            new Rectangle(-1920, -1080, 1920, 1008), 1.5, true);
        Assert.Equal(hidden, shown);
        Assert.Equal(-18, shown.Bottom);
    }

    [Theory]
    [InlineData(-1280, 0, 1280, 720)]
    [InlineData(1920, -900, 800, 600)]
    [InlineData(0, 0, 640, 480)]
    public void Small_and_offset_monitors_contain_sheet(int x, int y, int width, int height)
    {
        var monitor = new Rectangle(x, y, width, height);
        var work = new Rectangle(x + 48, y, width - 48, height);
        var result = BottomResultsPanel.CalculateBounds(monitor, work, 2, false);
        Assert.True(work.Contains(result));
        Assert.True(result.Width > 0 && result.Height > 0);
        Assert.Equal(work.Bottom - 24, result.Bottom);
    }
}
