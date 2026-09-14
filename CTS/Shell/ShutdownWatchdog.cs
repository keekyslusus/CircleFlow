namespace CircleToSearch.Shell;

internal sealed class ShutdownWatchdog(PluginLog log, Action terminateProcess, TimeSpan? timeout = null) : IDisposable
{
    internal static TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);
    private readonly ManualResetEventSlim _finished = new();
    private readonly object _gate = new();
    private readonly List<Action> _emergency = [];
    private int _state;

    public void AddEmergencyCleanup(Action cleanup)
    {
        lock (_gate) _emergency.Add(cleanup);
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;
        new Thread(Watch) { IsBackground = true, Name = "CircleFlow shutdown watchdog" }.Start();
    }

    private void Watch()
    {
        try
        {
            if (_finished.Wait(timeout ?? DefaultTimeout)) return;
            if (Interlocked.CompareExchange(ref _state, 3, 1) != 1) return;
            Action[] emergency;
            lock (_gate) emergency = _emergency.ToArray();
            var attempts = emergency.Select(Attempt).ToList();
            attempts.Add(Attempt(() => log.Warn(nameof(ShutdownWatchdog), "shutdown timed out; terminating the current process")));
            // Broken native calls or a blocked log writer must not disable the watchdog itself.
            Task.WhenAll(attempts).Wait(TimeSpan.FromMilliseconds(250));
            terminateProcess();
        }
        finally { lock (_gate) _finished.Dispose(); }
    }

    private static Task Attempt(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try { action(); }
            catch { }
            finally { completion.SetResult(); }
        }) { IsBackground = true, Name = "CircleFlow emergency cleanup" }.Start();
        return completion.Task;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            var previous = Interlocked.Exchange(ref _state, 2);
            if (previous == 0) _finished.Dispose();
            else if (previous == 1) _finished.Set();
        }
    }
}
