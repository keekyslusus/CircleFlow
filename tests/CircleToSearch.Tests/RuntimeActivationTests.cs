using CircleToSearch.Interop;
using CircleToSearch.Settings;
using CircleToSearch.Shell;
using CircleToSearch.Trigger;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class RuntimeActivationTests
{
    [Fact]
    public async Task Hotkey_conflict_reports_an_error_but_Open_still_starts_and_can_be_stopped()
    {
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "runtime-activation-" + Guid.NewGuid().ToString("N")));
        AppDataDirectory.Initialize(paths);
        var log = new PluginLog(paths.LogsDirectory);
        using var blocker = new HotkeyWindow(new StaDispatcher("CircleFlow hotkey conflict test"), log);
        var registration = new HotkeyRegistrar(blocker, TestUiStrings.English, log);
        var gesture = Enumerable.Range(1, 11).Select(key => $"Ctrl+Alt+Shift+F{key}")
            .First(candidate => registration.TryApply(candidate).Success);
        var notifier = new TestPluginNotifier();
        var hiding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hides = 0;
        await using var rollback = new ResourceRollbackScope(log);
        var runtime = CompositionRoot.Create(paths,
            new AppSettings { HotkeyGesture = gesture, HideDelayMilliseconds = 2000 }, new SettingsStore(paths),
            TestUiStrings.English, notifier,
            new UrlOpeningService(_ => true, notifier, TestUiStrings.English, log),
            () => { hides++; return hiding.Task; }, log, rollback);
        Assert.False(runtime.Settings.HotkeyStatus.IsActive);
        Assert.Equal(TestUiStrings.English.HotkeyConflict(gesture), Assert.Single(notifier.Errors).Message);
        var open = runtime.OpenAsync();
        Assert.Equal(1, hides);
        Assert.False(open.IsCompleted);
        await runtime.OpenAsync();
        Assert.Equal(1, hides);
        var stopping = runtime.StopAsync();
        hiding.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await open.WaitAsync(TimeSpan.FromSeconds(1));
        await runtime.OpenAsync();
        Assert.Equal(1, hides);
        Assert.True(blocker.TryUnregister());
        Assert.Single(notifier.Errors);
    }
}
