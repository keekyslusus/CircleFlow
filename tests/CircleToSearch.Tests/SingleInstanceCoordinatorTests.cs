using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void Pipe_accepts_only_the_exact_Open_message_and_survives_invalid_clients() => OnOwnerThread(() =>
    {
        var name = NewName();
        using var owner = SingleInstanceCoordinator.TryAcquire(name);
        var opens = 0;
        var failures = new ConcurrentQueue<Exception>();
        owner!.StartListening(() => { Interlocked.Increment(ref opens); return true; }, failures.Enqueue);
        foreach (var message in new[] { "open", "Exit", "Open\n" })
            Assert.Equal(0, ExchangeAsync(name, message).GetAwaiter().GetResult());
        foreach (var message in new[] { "Open https://example.com", "Open" + new string('x', 4096) })
        {
            try { Assert.Equal(0, ExchangeAsync(name, message).GetAwaiter().GetResult()); }
            catch (IOException) { }
        }
        Assert.Equal(0, opens);
        Assert.Equal(1, ExchangeAsync(name, "Open").GetAwaiter().GetResult());
        Assert.Equal(1, opens);
        Assert.Empty(failures);
    });

    [Fact]
    public void A_silent_client_times_out_and_shutdown_interrupts_a_connected_client() => OnOwnerThread(() =>
    {
        var name = NewName();
        using var owner = SingleInstanceCoordinator.TryAcquire(name);
        var failures = new ConcurrentQueue<Exception>();
        owner!.StartListening(() => true, failures.Enqueue);
        using (var silent = ConnectAsync(name).GetAwaiter().GetResult())
        {
            var read = silent.ReadAsync(new byte[1]).AsTask();
            Assert.Equal(0, read.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult());
        }
        Assert.Equal(1, ExchangeAsync(name, "Open").GetAwaiter().GetResult());
        using var next = ConnectAsync(name).GetAwaiter().GetResult();
        owner.StopListeningAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Assert.Empty(failures);
    });

    [Fact]
    public void A_second_launch_waits_for_the_primary_pipe_to_become_ready() => OnOwnerThread(() =>
    {
        var name = NewName();
        using var owner = SingleInstanceCoordinator.TryAcquire(name);
        using var started = new ManualResetEventSlim();
        var secondary = Task.Run(() =>
        {
            started.Set();
            var result = SingleInstanceCoordinator.AcquireOrActivate(name, TimeSpan.FromSeconds(2));
            using var unexpected = result.Instance;
            Assert.Null(unexpected);
            return result.Activated;
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));
        Thread.Sleep(100);
        owner!.StartListening(() => true, _ => { });
        Assert.True(secondary.GetAwaiter().GetResult());
    });

    [Fact]
    public void A_departing_owner_can_be_replaced_during_the_bounded_connection_retry() => OnOwnerThread(() =>
    {
        var name = NewName();
        using var owned = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var previous = new Thread(() =>
        {
            using var owner = SingleInstanceCoordinator.TryAcquire(name);
            owned.Set();
            release.Wait(TimeSpan.FromSeconds(3));
        });
        previous.Start();
        Assert.True(owned.Wait(TimeSpan.FromSeconds(1)));
        var releaser = Task.Run(async () => { await Task.Delay(100); release.Set(); });
        var result = SingleInstanceCoordinator.AcquireOrActivate(name, TimeSpan.FromSeconds(2));
        using var replacement = result.Instance;
        Assert.NotNull(replacement);
        Assert.False(result.Activated);
        Assert.True(previous.Join(TimeSpan.FromSeconds(1)));
        releaser.GetAwaiter().GetResult();
    });

    [Fact]
    public void An_abandoned_mutex_is_acquired_and_released_on_the_new_owner_thread() => OnOwnerThread(() =>
    {
        var name = NewName();
        using var abandoned = new Mutex(false, name);
        var previous = new Thread(() => abandoned.WaitOne());
        previous.Start();
        Assert.True(previous.Join(TimeSpan.FromSeconds(1)));
        using var replacement = SingleInstanceCoordinator.TryAcquire(name);
        Assert.NotNull(replacement);
        var wrongThread = Task.Run(() => Record.Exception(replacement.Dispose)).GetAwaiter().GetResult();
        Assert.IsType<InvalidOperationException>(wrongThread);
        replacement.Dispose();
        using var next = SingleInstanceCoordinator.TryAcquire(name);
        Assert.NotNull(next);
    });

    [Fact]
    public void Unavailable_or_stopping_owner_never_produces_a_second_runtime() => OnOwnerThread(() =>
    {
        var name = NewName();
        using var owner = SingleInstanceCoordinator.TryAcquire(name);
        foreach (var listening in new[] { false, true })
        {
            if (listening) owner!.StartListening(() => false, _ => { });
            var elapsed = Stopwatch.StartNew();
            Task.Run(() =>
            {
                var result = SingleInstanceCoordinator.AcquireOrActivate(name, TimeSpan.FromMilliseconds(200));
                using var unexpected = result.Instance;
                Assert.Null(unexpected);
                Assert.False(result.Activated);
            }).GetAwaiter().GetResult();
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2));
        }
    });

    [Fact]
    public void A_lost_acknowledgement_is_not_retried_as_another_Open() => OnOwnerThread(() =>
    {
        var name = NewName();
        using var owner = SingleInstanceCoordinator.TryAcquire(name);
        using var pipe = new NamedPipeServerStream(SingleInstanceCoordinator.PipeNameFor(name), PipeDirection.InOut,
            1, PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var server = Task.Run(async () =>
        {
            await pipe.WaitForConnectionAsync();
            var bytes = new byte[4];
            await pipe.ReadExactlyAsync(bytes);
            Assert.Equal("Open", Encoding.UTF8.GetString(bytes));
            pipe.Disconnect();
        });
        Task.Run(() =>
        {
            var result = SingleInstanceCoordinator.AcquireOrActivate(name, TimeSpan.FromSeconds(1));
            using var unexpected = result.Instance;
            Assert.Null(unexpected);
            Assert.False(result.Activated);
        }).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        server.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
    });

    [Fact]
    public void Concurrent_secondary_processes_activate_one_owner() => OnOwnerThread(() =>
    {
        const string marker = "CIRCLEFLOW_ACTIVATION_TEST_NAME";
        var inheritedName = Environment.GetEnvironmentVariable(marker);
        if (inheritedName is not null)
        {
            var result = SingleInstanceCoordinator.AcquireOrActivate(inheritedName);
            using var unexpected = result.Instance;
            Assert.Null(unexpected);
            Assert.True(result.Activated);
            return;
        }
        var name = NewName();
        using var owner = SingleInstanceCoordinator.TryAcquire(name);
        var opens = 0;
        var failures = new ConcurrentQueue<Exception>();
        owner!.StartListening(() => { Interlocked.Increment(ref opens); return true; }, failures.Enqueue);
        Task.WhenAll(Enumerable.Range(0, 3).Select(_ => RunSecondaryProcessAsync(marker, name)))
            .GetAwaiter().GetResult();
        Assert.Equal(3, opens);
        Assert.Empty(failures);
    });

    private static async Task RunSecondaryProcessAsync(string marker, string name)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(SingleInstanceCoordinatorTests).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(SingleInstanceCoordinatorTests).FullName + "." + nameof(Concurrent_secondary_processes_activate_one_owner));
        start.Environment[marker] = name;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string name)
    {
        var client = new NamedPipeClientStream(".", SingleInstanceCoordinator.PipeNameFor(name), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await client.ConnectAsync(3000); return client; }
        catch { client.Dispose(); throw; }
    }

    private static async Task<byte> ExchangeAsync(string name, string message)
    {
        using var client = await ConnectAsync(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.WriteAsync(Encoding.UTF8.GetBytes(message), timeout.Token);
        var result = new byte[1];
        await client.ReadExactlyAsync(result, timeout.Token);
        return result[0];
    }

    private static void OnOwnerThread(Action action)
    {
        // Mutex ownership is thread-affine; test-runner continuations must not move it.
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(failure);
    }
    private static string NewName() => "Local\\CircleFlow.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Only_the_owner_can_initialize_data_and_release_allows_another_owner() => OnOwnerThread(() =>
    {
        var name = "Local\\CircleFlow.Tests." + Guid.NewGuid().ToString("N");
        using var first = SingleInstanceCoordinator.TryAcquire(name);
        Assert.NotNull(first);
        var acquired = true;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "secondary-" + Guid.NewGuid().ToString("N")));
        var second = new Thread(() =>
        {
            using var instance = SingleInstanceCoordinator.TryAcquire(name);
            acquired = instance is not null;
            if (acquired) AppDataDirectory.Initialize(paths);
        });
        second.Start();
        Assert.True(second.Join(TimeSpan.FromSeconds(5)));
        Assert.False(acquired);
        Assert.False(Directory.Exists(paths.RootDirectory));
        first.Dispose();
        using var replacement = SingleInstanceCoordinator.TryAcquire(name);
        Assert.NotNull(replacement);
    });

    [Fact]
    public void Session_identity_is_stable_and_contains_the_current_user() => OnOwnerThread(() =>
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        Assert.StartsWith("Local\\CircleFlow." + identity.User!.Value + ".", SingleInstanceCoordinator.CurrentSessionName);
        using var process = Process.GetCurrentProcess();
        Assert.EndsWith("." + process.SessionId, SingleInstanceCoordinator.CurrentSessionName);
        Assert.Equal(SingleInstanceCoordinator.CurrentSessionName, SingleInstanceCoordinator.CurrentSessionName);
    });

    [Fact]
    public void Session_names_address_independent_mutexes_and_pipes() => OnOwnerThread(() =>
    {
        var prefix = NewName();
        using var first = SingleInstanceCoordinator.TryAcquire(prefix + ".1");
        using var second = SingleInstanceCoordinator.TryAcquire(prefix + ".2");
        Assert.NotNull(first);
        Assert.NotNull(second);
        var firstOpens = 0;
        var secondOpens = 0;
        first.StartListening(() => { firstOpens++; return true; }, _ => { });
        second.StartListening(() => { secondOpens++; return true; }, _ => { });
        Assert.Equal(1, ExchangeAsync(prefix + ".2", "Open").GetAwaiter().GetResult());
        Assert.Equal(0, firstOpens);
        Assert.Equal(1, secondOpens);
    });
}
