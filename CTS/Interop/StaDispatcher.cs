using System.Windows.Threading;

namespace CircleToSearch.Interop;

// dedicated STA thread with a running message pump. The hotkey window and the overlay window
// each need such a thread: RegisterHotKey delivers WM_HOTKEY to the creating thread's queue and
// WPF windows must live on STA.
internal sealed class StaDispatcher : IStaDispatcher
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action? _beforeDispatcherRun;
    private readonly Action? _afterStopped;
    private Dispatcher? _dispatcher;
    private int _stopping;
    private int _readyDisposeStarted;

    public StaDispatcher(string threadName)
        : this(threadName, ShutdownTimeout, null, null, null)
    {
    }

    internal StaDispatcher(
        string threadName,
        TimeSpan initializationTimeout,
        Func<ManualResetEventSlim, TimeSpan, bool>? waitForReady,
        Action? beforeDispatcherRun,
        Action? afterStopped)
    {
        if (initializationTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initializationTimeout));
        _beforeDispatcherRun = beforeDispatcherRun;
        _afterStopped = afterStopped;
        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = threadName,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        var ready = waitForReady?.Invoke(_ready, initializationTimeout)
            ?? _ready.Wait(initializationTimeout);
        if (ready) return;

        RequestShutdown();
        _ = _stopped.Task.ContinueWith(
            _ => DisposeReadySignal(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        throw new TimeoutException($"STA dispatcher '{threadName}' did not initialize in time.");
    }

    public async Task<T?> InvokeAsync<T>(Func<T?> callback, CancellationToken token)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return default;

        try
        {
            return await dispatcher.InvokeAsync(callback, DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return default;
        }
    }

    public bool TryPost(Action action)
    {
        if (Volatile.Read(ref _stopping) != 0) return false;
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return false;
        try
        {
            var operation = dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
            return operation.Status != DispatcherOperationStatus.Aborted;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Send(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;

        try
        {
            dispatcher.Invoke(action, DispatcherPriority.Send, CancellationToken.None, ShutdownTimeout);
        }
        catch (TaskCanceledException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    private void Pump()
    {
        try
        {
            Volatile.Write(ref _dispatcher, Dispatcher.CurrentDispatcher);
            _ready.Set();
            var stopping = Volatile.Read(ref _stopping) != 0;
            _beforeDispatcherRun?.Invoke();
            if (stopping) RequestShutdown();
            Dispatcher.Run();
        }
        finally
        {
            _stopped.TrySetResult();
            _afterStopped?.Invoke();
        }
    }

    public void Dispose()
    {
        var stop = StopAsync();
        if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
        {
            try { stop.Wait(ShutdownTimeout); }
            catch (AggregateException) { }
        }
        if (stop.IsCompleted) DisposeReadySignal();
    }

    public Task StopAsync()
    {
        RequestShutdown();
        if (_stopped.Task.IsCompleted) DisposeReadySignal();
        else
        {
            _ = _stopped.Task.ContinueWith(
                _ => DisposeReadySignal(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        return _stopped.Task;
    }

    private void RequestShutdown()
    {
        Interlocked.Exchange(ref _stopping, 1);
        var dispatcher = Volatile.Read(ref _dispatcher);
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        try { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
        catch (InvalidOperationException) { }
    }

    private void DisposeReadySignal()
    {
        if (Interlocked.Exchange(ref _readyDisposeStarted, 1) == 0) _ready.Dispose();
    }
}
