using System.Windows.Threading;

namespace CircleToSearch.Shell;

internal sealed class OpenCommandDispatcher(Dispatcher dispatcher, PluginLog log) : IDisposable
{
    private readonly object _gate = new();
    private Func<Task>? _open;
    private bool _pending;
    private bool _scheduled;
    private bool _stopping;

    public bool TryRequestOpen()
    {
        lock (_gate)
        {
            if (_stopping || dispatcher.HasShutdownStarted) return false;
            _pending = true;
            Schedule();
            return true;
        }
    }

    public void SetReady(Func<Task> open)
    {
        dispatcher.VerifyAccess();
        lock (_gate)
        {
            if (_stopping) return;
            if (_open is not null) throw new InvalidOperationException("Open is already ready.");
            _open = open;
            Schedule();
        }
    }

    private void Schedule()
    {
        if (_open is null || !_pending || _scheduled) return;
        _scheduled = true;
        dispatcher.BeginInvoke(new Action(DispatchOpen));
    }

    private async void DispatchOpen()
    {
        Func<Task> open;
        lock (_gate)
        {
            _scheduled = false;
            if (_stopping || !_pending) return;
            _pending = false;
            open = _open!;
        }
        try { await open(); }
        catch (Exception exception) { log.SafeError(nameof(OpenCommandDispatcher), "open", exception); }
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        lock (_gate)
        {
            _stopping = true;
            _pending = false;
        }
    }
}
