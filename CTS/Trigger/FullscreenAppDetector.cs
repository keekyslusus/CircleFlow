// SPDX-License-Identifier: MIT
// Portions adapted from Flow Launcher (Win32Helper.IsForegroundWindowFullscreen).
// Copyright (c) 2019 Flow-Launcher, Copyright (c) 2015 Wox. Licensed under the MIT License.
// Ported and modified for CircleFlow beginning 2026-09-24.

using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using CircleToSearch.Interop;

namespace CircleToSearch.Trigger;

internal readonly record struct ForegroundWindow(
    string ClassName, Rectangle Bounds, Rectangle MonitorBounds, string ProcessName, bool IsOwnProcess);

internal static class FullscreenAppDetector
{
    private const uint MonitorDefaultToNearest = 2;

    // Browsers and players go fullscreen for video, which is a common thing to search from.
    private static readonly HashSet<string> VideoApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "chromium", "msedge", "firefox", "opera", "brave", "vivaldi", "browser", "floorp",
        "librewolf", "waterfox", "zen", "thorium", "helium",
        "vlc", "mpv", "mpvnet", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "potplayer", "potplayer64",
        "potplayermini", "potplayermini64", "smplayer", "kmplayer", "kmplayer64", "wmplayer",
        "microsoft.media.player", "video.ui",
    };

    public static bool IsForegroundFullscreenApp() => Capture() is { } window && BlocksHotkey(window);

    internal static bool BlocksHotkey(ForegroundWindow window) =>
        !window.IsOwnProcess
        && window.ClassName != "ConsoleWindowClass"
        // The desktop, Task View and other shell surfaces belong to Explorer.
        && !string.Equals(window.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)
        && !VideoApps.Contains(window.ProcessName)
        // A maximized window extends past the monitor by its resize borders, so only an exact match is fullscreen.
        && window.Bounds == window.MonitorBounds;

    private static ForegroundWindow? Capture()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == NativeMethods.GetDesktopWindow() || hwnd == NativeMethods.GetShellWindow())
            return null;
        if (!NativeMethods.GetWindowRect(hwnd, out var bounds)) return null;
        var monitor = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref monitor))
            return null;
        var className = new StringBuilder(256);
        NativeMethods.GetClassNameW(hwnd, className, className.Capacity);
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return new ForegroundWindow(className.ToString(), ToRectangle(bounds), ToRectangle(monitor.Monitor),
            ProcessName(processId), processId == Environment.ProcessId);
    }

    private static string ProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static Rectangle ToRectangle(RECT rect) => Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
