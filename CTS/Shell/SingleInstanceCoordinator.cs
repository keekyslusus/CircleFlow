using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace CircleToSearch.Shell;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;
    private NamedPipeServerStream? _server;
    private bool _disposed;

    private SingleInstanceCoordinator(Mutex mutex, string name) => (_mutex, _pipeName) = (mutex, PipeNameFor(name));

    internal static TimeSpan ActivationTimeout => TimeSpan.FromSeconds(5);

    internal static string PipeNameFor(string name) =>
        "CircleFlow." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)));

    public static InstanceLaunchResult AcquireOrActivate(string name, TimeSpan? timeout = null)
    {
        var budget = timeout ?? ActivationTimeout;
        var elapsed = Stopwatch.StartNew();
        do
        {
            var instance = TryAcquire(name);
            if (instance is not null) return new(instance, false);
            var remaining = budget - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var attempt = remaining < TimeSpan.FromMilliseconds(500) ? remaining : TimeSpan.FromMilliseconds(500);
            // Ownership must stay on the bootstrap STA, even when pipe I/O resumes on a worker.
            var delivery = SendOpenAsync(PipeNameFor(name), attempt).GetAwaiter().GetResult();
            if (delivery == OpenDelivery.Accepted) return new(null, true);
            // Once sent, a lost acknowledgement cannot safely be retried as a new command.
            if (delivery == OpenDelivery.Unconfirmed) return new(null, false);
            remaining = budget - elapsed.Elapsed;
            if (remaining > TimeSpan.Zero)
                Thread.Sleep(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25));
        } while (elapsed.Elapsed < budget);
        return new(null, false);
    }

    private static async Task<OpenDelivery> SendOpenAsync(string pipeName, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var sent = false;
        try
        {
            await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
            sent = true;
            await client.WriteAsync("Open"u8.ToArray(), deadline.Token).ConfigureAwait(false);
            var acknowledgement = new byte[1];
            var count = await client.ReadAsync(acknowledgement, deadline.Token).ConfigureAwait(false);
            if (count != 1) return OpenDelivery.Unconfirmed;
            return acknowledgement[0] == 1 ? OpenDelivery.Accepted : OpenDelivery.Rejected;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return sent ? OpenDelivery.Unconfirmed : OpenDelivery.Unavailable;
        }
    }

    public void StartListening(Func<bool> requestOpen, Action<Exception> reportFailure)
    {
        VerifyOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener is not null) throw new InvalidOperationException("The activation listener is already started.");
        var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 16, 16);
        _server = server;
        _listener = Task.Run(() => ListenAsync(server, requestOpen, reportFailure));
    }

    private async Task ListenAsync(NamedPipeServerStream server, Func<bool> requestOpen, Action<Exception> reportFailure)
    {
        using (server)
        {
            try
            {
                while (!_shutdown.IsCancellationRequested)
                {
                    await server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                    try
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(1));
                        var request = new byte[5];
                        var count = await server.ReadAsync(request, deadline.Token).ConfigureAwait(false);
                        var valid = count == 4 && server.IsMessageComplete && request.AsSpan(0, count).SequenceEqual("Open"u8);
                        var accepted = valid && !_shutdown.IsCancellationRequested && requestOpen();
                        await server.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, deadline.Token).ConfigureAwait(false);
                        // DisconnectNamedPipe discards unread output. Let the client read the ACK and close first.
                        await server.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is IOException or OperationCanceledException)
                    {
                        // A stalled or disconnected client must not monopolize the activation pipe.
                    }
                    finally { server.Disconnect(); }
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception exception)
            {
                try { reportFailure(exception); }
                catch { }
            }
        }
    }

    public Task StopListeningAsync()
    {
        if (!_disposed) _shutdown.Cancel();
        return _listener ?? Task.CompletedTask;
    }

    public void AbortListening() => Volatile.Read(ref _server)?.Dispose();

    public static string CurrentSessionName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var process = Process.GetCurrentProcess();
            return $"Local\\CircleFlow.{identity.User!.Value}.{process.SessionId}";
        }
    }

    public static SingleInstanceCoordinator? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            try
            {
                if (mutex.WaitOne(0)) return new SingleInstanceCoordinator(mutex, name);
            }
            catch (AbandonedMutexException) { return new SingleInstanceCoordinator(mutex, name); }
        }
        catch { mutex.Dispose(); throw; }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        VerifyOwner();
        try { StopListeningAsync().GetAwaiter().GetResult(); }
        finally
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _shutdown.Dispose();
            _disposed = true;
        }
    }

    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("The instance mutex must be released by its owning thread.");
    }

    private enum OpenDelivery { Unavailable, Accepted, Rejected, Unconfirmed }
}

internal sealed record InstanceLaunchResult(SingleInstanceCoordinator? Instance, bool Activated);
