using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CircleToSearch.Interop;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class BottomResultsPanelTests
{
    [Fact]
    public async Task Native_window_finishes_entrance_inside_monitor_work_area()
    {
        using var dispatcher = new StaDispatcher("Bottom sheet test");
        Window? window = null;
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
                _ = new BottomResultsPanel(window, anchor);
                window.Show();
            });
            await Task.Delay(600);
            dispatcher.Send(() =>
            {
                Assert.True(GetWindowRect(new WindowInteropHelper(window!).Handle, out var bounds));
                Assert.True(bounds.Left >= info.Work.Left);
                Assert.True(bounds.Right <= info.Work.Right);
                Assert.True(bounds.Top >= info.Work.Top);
                Assert.InRange(bounds.Bottom, info.Work.Bottom - 50, info.Monitor.Bottom - 1);
                Assert.True(bounds.Bottom - bounds.Top > 100);
            });
        }
        finally { dispatcher.Send(() => window?.Close()); }
    }

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
