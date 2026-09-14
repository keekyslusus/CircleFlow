using System.Reflection;
using System.Diagnostics;
using System.Windows;
using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AppShutdownTests
{
    [Fact]
    public async Task Cleanup_failures_do_not_skip_other_branches_or_instance_release()
    {
        if (await IsolatedTestHost.RunAsync<AppShutdownTests>()) return;
        var events = new List<string>();
        var result = Run((app, lifetime) =>
        {
            lifetime.AddStop("broken-stop", () => throw new InvalidOperationException("stop-secret"));
            lifetime.AddStop("stop", () => events.Add("stop"));
            lifetime.AddCleanup("broken-cleanup", async () => { await Task.Yield(); throw new IOException("cleanup-secret"); });
            lifetime.AddCleanup("cleanup", async () =>
            {
                await Task.Run(async () => await app.Dispatcher.InvokeAsync(() => events.Add("dispatcher-cleanup")));
                Assert.False(app.Dispatcher.HasShutdownStarted);
            });
            lifetime.AddRelease("broken-release", () => throw new InvalidOperationException("release-secret"));
            lifetime.AddRelease("release", () => events.Add("released"));
            _ = lifetime.RequestExitAsync();
        });
        Assert.Equal(1, result.Code);
        Assert.Null(result.StartupFailure);
        Assert.Equal(new[] { "stop", "dispatcher-cleanup", "released" }, events);
    }

    [Fact]
    public async Task Startup_failure_rolls_back_on_a_worker_while_main_dispatcher_remains_available()
    {
        if (await IsolatedTestHost.RunAsync<AppShutdownTests>()) return;
        var cleaned = false;
        var released = false;
        var result = Run((app, lifetime) =>
        {
            var log = new PluginLog(TestOutputPaths.TempDirectory);
            var rollback = new ResourceRollbackScope(log);
            lifetime.AddCleanup("rollback", () => rollback.DisposeAsync().AsTask());
            rollback.Own(new object(), async () =>
            {
                Assert.False(app.Dispatcher.CheckAccess());
                await app.Dispatcher.InvokeAsync(() => cleaned = true);
            });
            lifetime.AddRelease("release", () => released = true);
            throw new InvalidOperationException("startup-secret");
        });
        Assert.Equal(1, result.Code);
        Assert.IsType<InvalidOperationException>(result.StartupFailure);
        Assert.True(cleaned);
        Assert.True(released);
    }

    [Fact]
    public async Task Cancellation_during_startup_stops_late_resources_and_releases_mutex_after_cleanup()
    {
        if (await IsolatedTestHost.RunAsync<AppShutdownTests>()) return;
        var stopped = false;
        var cleaned = false;
        var reacquired = false;
        var result = Run((app, lifetime) =>
        {
            var name = "Local\\CircleFlow.ShutdownTests." + Guid.NewGuid().ToString("N");
            var instance = SingleInstanceCoordinator.TryAcquire(name)!;
            lifetime.AddRelease("instance", instance.Dispose);
            lifetime.AddRelease("reacquire", () =>
            {
                using var next = SingleInstanceCoordinator.TryAcquire(name);
                reacquired = next is not null && cleaned;
            });
            _ = lifetime.RequestExitAsync();
            lifetime.AddStop("late-runtime", () => stopped = true);
            lifetime.AddCleanup("late-cleanup", async () => await app.Dispatcher.InvokeAsync(() => cleaned = true));
        });
        Assert.Equal(0, result.Code);
        Assert.Null(result.StartupFailure);
        Assert.True(stopped);
        Assert.True(reacquired);
    }

    [Fact]
    public async Task Logoff_finishes_dispatcher_cleanup_before_returning_without_vetoing_the_session()
    {
        if (await IsolatedTestHost.RunAsync<AppShutdownTests>()) return;
        var events = new List<string>();
        var result = Run((app, lifetime) =>
        {
            lifetime.AddStop("stop", () => events.Add("stop"));
            lifetime.AddCleanup("runtime", async () =>
            {
                await Task.Run(async () => await app.Dispatcher.InvokeAsync(() =>
                {
                    Assert.False(app.Dispatcher.HasShutdownStarted);
                    events.Add("cleaned");
                }));
            });
            lifetime.AddRelease("release", () => events.Add("released"));
            RaiseSessionEnding(app, ReasonSessionEnding.Logoff);
            Assert.Equal(new[] { "stop", "cleaned", "released" }, events);
            Assert.Same(lifetime.RequestExitAsync(), lifetime.RequestExitAsync());
            Assert.True(lifetime.RequestExitAsync().IsCompleted);
        }, afterStartup: true);
        Assert.Equal(0, result.Code);
    }

    [Fact]
    public async Task Shutdown_during_explicit_exit_and_reentrant_session_events_share_one_cleanup()
    {
        if (await IsolatedTestHost.RunAsync<AppShutdownTests>()) return;
        var calls = 0;
        var releases = 0;
        var result = Run((app, lifetime) =>
        {
            lifetime.AddCleanup("runtime", async () =>
            {
                calls++;
                await Task.Yield();
                RaiseSessionEnding(app, ReasonSessionEnding.Shutdown);
            });
            lifetime.AddRelease("release", () => releases++);
            var exit = lifetime.RequestExitAsync();
            Assert.False(exit.IsCompleted);
            RaiseSessionEnding(app, ReasonSessionEnding.Shutdown);
            Assert.True(exit.IsCompleted);
            Assert.Same(exit, lifetime.RequestExitAsync());
        }, afterStartup: true);
        Assert.Equal(0, result.Code);
        Assert.Equal(1, calls);
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task Session_ending_stops_waiting_for_unfinished_cleanup_without_vetoing_shutdown()
    {
        if (await IsolatedTestHost.RunAsync<AppShutdownTests>()) return;
        var calls = 0;
        var released = false;
        var result = Run((app, lifetime) =>
        {
            var neverFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lifetime.AddCleanup("runtime", () => { calls++; return neverFinished.Task; });
            lifetime.AddRelease("release", () => released = true);
            var elapsed = Stopwatch.StartNew();
            RaiseSessionEnding(app, ReasonSessionEnding.Shutdown);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(4));
            Assert.False(lifetime.RequestExitAsync().IsCompleted);
            Assert.False(released);
        }, afterStartup: true);
        Assert.Equal(1, result.Code);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Session_cleanup_failure_does_not_veto_logoff_or_skip_other_cleanup()
    {
        if (await IsolatedTestHost.RunAsync<AppShutdownTests>()) return;
        var cleaned = false;
        var released = false;
        var result = Run((app, lifetime) =>
        {
            lifetime.AddCleanup("broken", async () => { await Task.Yield(); throw new IOException("cleanup-secret"); });
            lifetime.AddCleanup("other", async () => { await Task.Yield(); cleaned = true; });
            lifetime.AddRelease("release", () => released = true);
            RaiseSessionEnding(app, ReasonSessionEnding.Logoff);
            Assert.True(cleaned);
            Assert.True(released);
        }, afterStartup: true);
        Assert.Equal(1, result.Code);
    }

    [Fact]
    public async Task Wpf_session_message_handler_returns_permission_to_end_the_session()
    {
        if (await IsolatedTestHost.RunAsync<AppShutdownTests>()) return;
        var cleaned = false;
        var result = Run((app, lifetime) =>
        {
            lifetime.AddCleanup("runtime", async () => { await Task.Yield(); cleaned = true; });
            // Invoke only WPF's managed message handler, without sending an OS shutdown request.
            object[] arguments = [IntPtr.Zero, IntPtr.Zero];
            typeof(Application).GetMethod("WmQueryEndSession", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(app, arguments);
            Assert.Equal(new IntPtr(1), arguments[1]);
            Assert.True(cleaned);
        }, afterStartup: true);
        Assert.Equal(0, result.Code);
    }

    private static void RaiseSessionEnding(Application app, ReasonSessionEnding reason)
    {
        var args = (SessionEndingCancelEventArgs)Activator.CreateInstance(typeof(SessionEndingCancelEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [reason], null)!;
        typeof(Application).GetMethod("OnSessionEnding", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [args]);
        Assert.False(args.Cancel);
    }

    private static (int Code, Exception? StartupFailure) Run(Action<Application, AppLifetime> startup, bool afterStartup = false)
    {
        Exception? failure = null;
        Exception? startupFailure = null;
        var code = -1;
        var expired = false;
        var thread = new Thread(() =>
        {
            try
            {
                var app = CompositionRoot.CreateApplication();
                var log = new PluginLog(TestOutputPaths.TempDirectory);
                using var watchdog = new ShutdownWatchdog(log, () =>
                {
                    expired = true;
                    app.Dispatcher.BeginInvoke(() => app.Shutdown(1));
                }, TimeSpan.FromSeconds(5));
                var lifetime = new AppLifetime(app, log, watchdog);
                code = lifetime.Run(_ =>
                {
                    if (!afterStartup) startup(app, lifetime);
                    else app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { startup(app, lifetime); }
                        catch (Exception exception) { failure = exception; app.Shutdown(1); }
                    }));
                });
                startupFailure = lifetime.StartupFailure;
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.False(expired);
        return (code, startupFailure);
    }
}
