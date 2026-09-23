using System.Windows;
using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class ShutdownWatchdogTests
{
    [Fact]
    public void Default_policy_allows_ten_seconds_of_normal_cleanup() =>
        Assert.Equal(TimeSpan.FromSeconds(10), ShutdownWatchdog.DefaultTimeout);

    [Fact]
    public async Task Completed_cleanup_disarms_the_watchdog()
    {
        var calls = 0;
        using var watchdog = new ShutdownWatchdog(NewLog(), () => calls++, TimeSpan.FromMilliseconds(50));
        watchdog.AddEmergencyCleanup(() => calls++);
        watchdog.Start();
        watchdog.Dispose();
        watchdog.Dispose();
        await Task.Delay(150);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Blocking_emergency_cleanup_cannot_prevent_other_attempts_or_termination()
    {
        using var release = new ManualResetEventSlim();
        var hungFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempted = 0;
        using var watchdog = new ShutdownWatchdog(NewLog(), () => terminated.TrySetResult(), TimeSpan.FromMilliseconds(50));
        watchdog.AddEmergencyCleanup(() => { release.Wait(); hungFinished.SetResult(); });
        watchdog.AddEmergencyCleanup(() => throw new InvalidOperationException("broken native cleanup"));
        watchdog.AddEmergencyCleanup(() => Interlocked.Increment(ref attempted));
        watchdog.Start();
        watchdog.Start();
        try
        {
            await terminated.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, attempted);
        }
        finally { release.Set(); await hungFinished.Task.WaitAsync(TimeSpan.FromSeconds(1)); }
    }

    [Fact]
    public async Task A_blocked_process_is_terminated_after_emergency_cleanup()
    {
        const string marker = "CIRCLEFLOW_WATCHDOG_TEST_OUTPUT";
        var inherited = Environment.GetEnvironmentVariable(marker);
        if (inherited is not null)
        {
            var ui = new Thread(() =>
            {
                var app = CompositionRoot.CreateApplication();
                var log = new PluginLog(inherited);
                using var watchdog = new ShutdownWatchdog(log, () => Environment.Exit(1), TimeSpan.FromMilliseconds(200));
                var lifetime = new AppLifetime(app, log, watchdog);
                using var instance = SingleInstanceCoordinator.TryAcquire("Local\\CircleFlow.WatchdogTests." + Guid.NewGuid().ToString("N"));
                instance!.StartListening(() => true, _ => { });
                using var tray = new TrayIcon(new AppPaths(AppContext.BaseDirectory).TrayIconPath, TestUiStrings.English,
                    app.Dispatcher, log, () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask, lifetime.RequestExitAsync);
                watchdog.AddEmergencyCleanup(() =>
                {
                    instance.AbortListening();
                    tray.RemoveForShutdown();
                    File.WriteAllText(Path.Combine(inherited, "emergency.txt"), "attempted");
                });
                lifetime.AddCleanup("blocked-dispatcher", () =>
                {
                    app.Dispatcher.VerifyAccess();
                    using var blocked = new ManualResetEventSlim();
                    blocked.Wait(TimeSpan.FromSeconds(15));
                    return Task.CompletedTask;
                });
                lifetime.Run(cancellation => _ = lifetime.RequestExitAsync());
            }) { IsBackground = true };
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            Assert.True(ui.Join(TimeSpan.FromSeconds(20)));
            Assert.Fail("The watchdog did not terminate its own process.");
            return;
        }
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "watchdog-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var (exitCode, _) = await IsolatedTestHost.RunChildAsync(
            typeof(ShutdownWatchdogTests).FullName + "." + nameof(A_blocked_process_is_terminated_after_emergency_cleanup),
            marker, directory, TimeSpan.FromSeconds(10));
        Assert.NotEqual(0, exitCode);
        Assert.Equal("attempted", File.ReadAllText(Path.Combine(directory, "emergency.txt")));
        Assert.Contains("shutdown timed out", File.ReadAllText(Path.Combine(directory, "plugin.log")));
    }

    private static PluginLog NewLog() => new(TestOutputPaths.TempDirectory);
}
