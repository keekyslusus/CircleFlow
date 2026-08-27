using System.Diagnostics;
using CircleToSearch.Interop;

namespace CircleToSearch.Search;

// After the Lens page opens, injects Ctrl+V into the foreground browser so the pasted image
// starts the search without further user input. Guarded by the foreground process so a slow
// browser never pastes into an unrelated application.
public sealed class BrowserPasteInjector
{
    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "chromium", "msedge", "firefox", "opera", "brave", "vivaldi", "yandex", "helium",
    };

    // Paste attempts start early and repeat: the first one usually lands while the Lens page is
    // still loading and is silently dropped by the browser, later ones hit the ready page.
    private static readonly int[] PasteDelaysMilliseconds = [900, 800, 800, 1200, 2000];

    private readonly PluginLog _log;
    private string? _browserExecutablePath;

    public BrowserPasteInjector(PluginLog log)
    {
        _log = log;
    }

    public void NoteLaunchedBrowser(Process? process)
    {
        try
        {
            _browserExecutablePath = process?.MainModule?.FileName;
        }
        catch
        {
            _browserExecutablePath = null;
        }
    }

    public bool IsForegroundBrowser()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            using var process = Process.GetProcessById((int)processId);
            if (MatchesLaunchedBrowser(process)) return true;
            return BrowserProcessNames.Contains(process.ProcessName);
        }
        catch
        {
            return false;
        }
    }

    public void SendPaste()
    {
        NativeMethods.keybd_event(0x11, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(0x56, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(0x56, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(0x11, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        _log.Info(nameof(BrowserPasteInjector), "Ctrl+V sent to the foreground window");
    }

    public async Task RunWatchAsync()
    {
        try
        {
            foreach (var delayMilliseconds in PasteDelaysMilliseconds)
            {
                await Task.Delay(delayMilliseconds).ConfigureAwait(false);
                if (!IsForegroundBrowser())
                {
                    _log.Info(nameof(BrowserPasteInjector), "foreground window is not the browser; auto-paste skipped");
                    return;
                }
                SendPaste();
            }
        }
        catch (Exception exception)
        {
            _log.Error(nameof(BrowserPasteInjector), "auto-paste failed", exception);
        }
    }

    private bool MatchesLaunchedBrowser(Process process)
    {
        if (_browserExecutablePath is null) return false;
        try
        {
            return string.Equals(process.MainModule?.FileName, _browserExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
