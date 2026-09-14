using CircleToSearch.Ui;

namespace CircleToSearch.Search;

public enum SearchState { Idle, Cancelable, Uploading }

public sealed class SearchCoordinator
{
    private readonly ISearchSessionWorkflow _workflow;
    private readonly Func<Task> _hideOwnWindows;
    private readonly Func<SearchSessionOptions> _sessionOptions;
    private readonly IPluginNotifier _notifier;
    private readonly UiStrings _strings;
    private readonly PluginLog _log;
    private readonly object _lifecycleGate = new();
    private int _state;
    private SessionCancellation? _cancellation;
    private Task _activeSession = Task.CompletedTask;
    private Task? _stopTask;
    private TaskCompletionSource? _stopCompletion;
    private bool _stopWorkStarted;
    private bool _stopping;

    internal SearchCoordinator(
        ISearchSessionWorkflow workflow,
        Func<Task> hideOwnWindows,
        Func<SearchSessionOptions> sessionOptions,
        IPluginNotifier notifier,
        UiStrings strings,
        PluginLog log)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _hideOwnWindows = hideOwnWindows ?? throw new ArgumentNullException(nameof(hideOwnWindows));
        _sessionOptions = sessionOptions ?? throw new ArgumentNullException(nameof(sessionOptions));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public SearchState State => (SearchState)Volatile.Read(ref _state);

    public Task StartFromHotkeyAsync()
    {
        try
        {
            lock (_lifecycleGate)
            {
                if (_stopping) return IgnoreTrigger("runtime is stopping");
            }
            return State switch
            {
                SearchState.Cancelable => CancelActiveSession(),
                SearchState.Uploading => IgnoreTrigger("upload in progress"),
                _ => StartSession("hotkey"),
            };
        }
        catch (Exception exception)
        {
            SurfaceFailure("starting the selection failed", _strings.StartingSelectionFailed(exception.Message), exception);
            return Task.CompletedTask;
        }
    }

    public Task OpenAsync()
    {
        try
        {
            lock (_lifecycleGate)
            {
                if (_stopping) return IgnoreTrigger("runtime is stopping");
            }
            return State == SearchState.Idle ? StartSession("open") : IgnoreTrigger("another session is active");
        }
        catch (Exception exception)
        {
            SurfaceFailure("starting the selection failed", _strings.StartingSelectionFailed(exception.Message), exception);
            return Task.CompletedTask;
        }
    }

    public Task CancelActiveSession()
    {
        SessionCancellation? cancellation;
        Task activeSession;
        lock (_lifecycleGate)
        {
            cancellation = _cancellation;
            activeSession = _activeSession;
        }
        try
        {
            _log.Info(nameof(SearchCoordinator), "canceling the active session");
            cancellation?.Cancel();
        }
        catch (Exception exception)
        {
            _log.Error(nameof(SearchCoordinator), "canceling the session failed", exception);
        }
        return activeSession;
    }

    internal void RequestStop()
    {
        lock (_lifecycleGate)
        {
            if (_stopping) return;
            _stopping = true;
            _stopCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = _stopCompletion.Task;
        }
    }

    public Task StopAsync()
    {
        SessionCancellation? cancellation;
        TaskCompletionSource completion;
        Task activeSession;
        lock (_lifecycleGate)
        {
            if (!_stopping)
            {
                _stopping = true;
                _stopCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopTask = _stopCompletion.Task;
            }
            if (_stopWorkStarted) return _stopTask!;
            _stopWorkStarted = true;
            cancellation = _cancellation;
            activeSession = _activeSession;
            completion = _stopCompletion!;
        }

        _ = FinishStopAsync(activeSession, cancellation, completion);
        return _stopTask!;
    }

    private Task StartSession(string trigger)
    {
        SessionCancellation cancellation;
        SearchSessionOptions options;
        TaskCompletionSource completion;
        lock (_lifecycleGate)
        {
            if (_stopping) return IgnoreTrigger("runtime is stopping");
            if (!_activeSession.IsCompleted) return IgnoreTrigger("session already active");
            options = _sessionOptions();
            cancellation = new SessionCancellation();
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancellation = cancellation;
            _activeSession = completion.Task;
            SetState(SearchState.Cancelable);
        }

        _ = RunSessionAsync(trigger, options, cancellation, completion);
        return completion.Task;
    }

    private async Task RunSessionAsync(
        string trigger,
        SearchSessionOptions options,
        SessionCancellation cancellation,
        TaskCompletionSource completion)
    {
        try
        {
            _log.Info(nameof(SearchCoordinator), $"selection started via {trigger}");
            await SafeHideOwnWindowsAsync().ConfigureAwait(false);
            try
            {
                await Task.Delay(options.HideDelayMilliseconds, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await _workflow.RunAsync(
                options,
                () => SetState(SearchState.Uploading),
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Info(nameof(SearchCoordinator), "session canceled");
        }
        catch (Exception exception)
        {
            SurfaceFailure("the search failed", _strings.SearchFailed(exception.Message), exception);
        }
        finally
        {
            lock (_lifecycleGate)
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                    SetState(SearchState.Idle);
                }
            }
            cancellation.Complete();
            completion.TrySetResult();
        }
    }

    private async Task FinishStopAsync(
        Task activeSession,
        SessionCancellation? cancellation,
        TaskCompletionSource completion)
    {
        Exception? stopFailure = null;

        try
        {
            if (cancellation is not null)
                await Task.Run(cancellation.Cancel).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            stopFailure = exception;
        }

        try
        {
            await activeSession.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            stopFailure = stopFailure is null
                ? exception
                : new AggregateException(stopFailure, exception);
        }

        if (stopFailure is not null)
        {
            _log.SafeError(nameof(SearchCoordinator), "stop-active-session", stopFailure);
            completion.TrySetException(stopFailure);
            return;
        }

        completion.TrySetResult();
    }

    private Task IgnoreTrigger(string reason)
    {
        _log.Info(nameof(SearchCoordinator), $"hotkey ignored: {reason}");
        return Task.CompletedTask;
    }

    private async Task SafeHideOwnWindowsAsync()
    {
        try { await _hideOwnWindows().ConfigureAwait(false); }
        catch (Exception exception)
        {
            _log.Warn(nameof(SearchCoordinator), $"hiding application windows failed: {exception.Message}");
        }
    }

    private void SurfaceFailure(string logMessage, string userMessage, Exception exception)
    {
        _log.Error(nameof(SearchCoordinator), logMessage, exception);
        _notifier.ShowError(_strings.PluginTitle, userMessage);
    }

    private void SetState(SearchState state) => Volatile.Write(ref _state, (int)state);

    private sealed class SessionCancellation
    {
        private readonly CancellationTokenSource _source = new();
        private readonly object _gate = new();
        private int _cancelers;
        private bool _completed;

        public CancellationToken Token => _source.Token;

        public void Cancel()
        {
            lock (_gate)
            {
                if (_completed) return;
                _cancelers++;
            }
            try { _source.Cancel(); }
            finally
            {
                lock (_gate)
                {
                    _cancelers--;
                    if (_completed && _cancelers == 0) _source.Dispose();
                }
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
                if (_cancelers == 0) _source.Dispose();
            }
        }
    }
}
