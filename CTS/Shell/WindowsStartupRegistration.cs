using System.IO;
using System.Security;
using Microsoft.Win32;

namespace CircleToSearch.Shell;

// The registry is the only source of truth: Task Manager and uninstallers change it without telling the app.
internal sealed class WindowsStartupRegistration(
    string executablePath,
    PluginLog log,
    string runKeyPath = WindowsStartupRegistration.RunKeyPath,
    string approvedKeyPath = WindowsStartupRegistration.ApprovedKeyPath)
{
    internal const string AutostartArgument = "--autostart";
    internal const string ValueName = "CircleFlow";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    internal string Command => $"\"{executablePath}\" {AutostartArgument}";

    public bool IsEnabled
    {
        get
        {
            try
            {
                if (!StartsThisCopy()) return false;
                using var approved = Registry.CurrentUser.OpenSubKey(approvedKeyPath);
                // Task Manager keeps the Run entry and marks it disabled with an odd first byte here.
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
            {
                log.SafeError(nameof(WindowsStartupRegistration), "read", exception);
                return false;
            }
        }
    }

    // Uninstalling must not remove an entry that starts another copy of the app.
    public void RemoveForUninstall()
    {
        try
        {
            if (!StartsThisCopy()) return;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            log.SafeError(nameof(WindowsStartupRegistration), "read-for-uninstall", exception);
            return;
        }
        TrySet(false);
    }

    private bool StartsThisCopy()
    {
        using var run = Registry.CurrentUser.OpenSubKey(runKeyPath);
        // An entry left by another copy of the app does not start this one.
        return run?.GetValue(ValueName) is string command
            && command.StartsWith($"\"{executablePath}\"", StringComparison.OrdinalIgnoreCase);
    }

    public bool TrySet(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var run = Registry.CurrentUser.CreateSubKey(runKeyPath);
                run.SetValue(ValueName, Command);
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
                run?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            using var approved = Registry.CurrentUser.OpenSubKey(approvedKeyPath, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            log.Info(nameof(WindowsStartupRegistration), enabled ? "enabled" : "disabled");
            return true;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            log.SafeError(nameof(WindowsStartupRegistration), enabled ? "enable" : "disable", exception);
            return false;
        }
    }
}
