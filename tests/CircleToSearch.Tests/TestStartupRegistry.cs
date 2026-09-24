using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using CircleToSearch.Shell;
using Microsoft.Win32;

namespace CircleToSearch.Tests;

internal sealed class TestStartupRegistry : IDisposable
{
    private const string TestsRoot = @"Software\CircleFlow.Tests";
    private static readonly string ProcessRoot = CreateProcessRoot();
    private readonly string _root = ProcessRoot + @"\" + Guid.NewGuid().ToString("N");

    public TestStartupRegistry(string? executablePath = null, PluginLog? log = null)
    {
        using (var root = Registry.CurrentUser.CreateSubKey(_root, true, RegistryOptions.Volatile))
        {
            root.CreateSubKey("Run", true, RegistryOptions.Volatile).Dispose();
            root.CreateSubKey("Approved", true, RegistryOptions.Volatile).Dispose();
        }
        ExecutablePath = executablePath ?? @"C:\Apps\CircleFlow\CircleFlow.exe";
        Registration = new WindowsStartupRegistration(ExecutablePath, log ?? new PluginLog(
            Path.Combine(TestOutputPaths.TempDirectory, "startup-" + Guid.NewGuid().ToString("N"))), RunKeyPath, ApprovedKeyPath);
    }

    public string ExecutablePath { get; }
    public WindowsStartupRegistration Registration { get; }
    public string RunKeyPath => _root + @"\Run";
    public string ApprovedKeyPath => _root + @"\Approved";

    public object? RunValue
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(WindowsStartupRegistration.ValueName);
        }
        set => Write(RunKeyPath, value);
    }

    public object? ApprovedValue
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            return key?.GetValue(WindowsStartupRegistration.ValueName);
        }
        set => Write(ApprovedKeyPath, value);
    }

    // Mirrors what Task Manager writes: an odd first byte followed by the time the entry was toggled.
    public void SetTaskManagerState(bool enabled) =>
        ApprovedValue = new byte[] { enabled ? (byte)2 : (byte)3, 0, 0, 0 }.Concat(BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc())).ToArray();

    public IDisposable DenyWrites()
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new RegistryAccessRule(user, RegistryRights.SetValue, AccessControlType.Deny);
        ChangeRunKeyAccess(security => security.AddAccessRule(rule));
        return new Restore(() => ChangeRunKeyAccess(security => security.RemoveAccessRule(rule)));
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    // Settings windows never dispose their keys and VSTest kills its host, so each run removes what finished runs left.
    private static string CreateProcessRoot()
    {
        using (var tests = Registry.CurrentUser.CreateSubKey(TestsRoot))
        {
            foreach (var name in tests.GetSubKeyNames())
            {
                if (int.TryParse(name, out var id) && IsRunning(id)) continue;
                try { tests.DeleteSubKeyTree(name, throwOnMissingSubKey: false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        var root = TestsRoot + @"\" + Environment.ProcessId;
        Registry.CurrentUser.CreateSubKey(root, true, RegistryOptions.Volatile).Dispose();
        return root;
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    private void ChangeRunKeyAccess(Action<RegistrySecurity> change)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ReadPermissions | RegistryRights.ChangePermissions)!;
        var security = key.GetAccessControl();
        change(security);
        key.SetAccessControl(security);
    }

    private static void Write(string path, object? value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!;
        if (value is null) key.DeleteValue(WindowsStartupRegistration.ValueName, throwOnMissingValue: false);
        else key.SetValue(WindowsStartupRegistration.ValueName, value);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
