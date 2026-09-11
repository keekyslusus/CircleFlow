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
    private Dispatcher? _dispatcher;

    public StaDispatcher(string threadName)
    {
        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = threadName,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(ShutdownTimeout);
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
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return false;
        dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
        return true;
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
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.Set();
        Dispatcher.Run();
    }

    public void Dispose()
    {
        _dispatcher?.InvokeShutdown();
        _thread.Join(ShutdownTimeout);
        _ready.Dispose();
    }
}
