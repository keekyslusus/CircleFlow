using CircleToSearch.Settings;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

public enum SearchState { Idle, Selecting, Uploading, RecognizingMusic, ShowingMusicResult }

public sealed class SearchCoordinator
{
    private readonly ISearchSessionWorkflow _workflow;
    private readonly Action _hideMainWindow;
    private readonly PluginSettings _settings;
    private readonly IPluginNotifier _notifier;
    private readonly UiStrings _strings;
    private readonly PluginLog _log;
    private readonly SemaphoreSlim _session = new(1, 1);
    private int _state;
    private CancellationTokenSource? _cancellation;
    private Task _activeSession = Task.CompletedTask;

    internal SearchCoordinator(
        ISearchSessionWorkflow workflow,
        Action hideMainWindow,
        PluginSettings settings,
        IPluginNotifier notifier,
        UiStrings strings,
        PluginLog log)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _hideMainWindow = hideMainWindow ?? throw new ArgumentNullException(nameof(hideMainWindow));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public SearchState State => (SearchState)Volatile.Read(ref _state);

    public Task StartFromHotkeyAsync()
    {
        try
        {
            return State switch
            {
                SearchState.Selecting or SearchState.RecognizingMusic or SearchState.ShowingMusicResult => CancelActiveSession(),
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

    public Task StartFromQueryAsync()
    {
        try
        {
            return State == SearchState.Idle ? StartSession("query") : IgnoreTrigger("another session is active");
        }
        catch (Exception exception)
        {
            SurfaceFailure("starting the selection failed", _strings.StartingSelectionFailed(exception.Message), exception);
            return Task.CompletedTask;
        }
    }

    public Task CancelActiveSession()
    {
        try
        {
            _log.Info(nameof(SearchCoordinator), "canceling the active session");
            Volatile.Read(ref _cancellation)?.Cancel();
        }
        catch (Exception exception)
        {
            _log.Error(nameof(SearchCoordinator), "canceling the session failed", exception);
        }
        return Volatile.Read(ref _activeSession);
    }

    private Task StartSession(string trigger)
    {
        var session = RunSessionAsync(trigger);
        Volatile.Write(ref _activeSession, session);
        return session;
    }

    private async Task RunSessionAsync(string trigger)
    {
        if (!await _session.WaitAsync(0).ConfigureAwait(false))
        {
            _log.Info(nameof(SearchCoordinator), $"trigger '{trigger}' ignored: session already active");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        Volatile.Write(ref _cancellation, cancellation);
        try
        {
            SetState(SearchState.Selecting);
            _log.Info(nameof(SearchCoordinator), $"selection started via {trigger}");
            SafeHideMainWindow();
            try
            {
                await Task.Delay(_settings.HideDelayMilliseconds, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await _workflow.RunAsync(SetState, cancellation.Token).ConfigureAwait(false);
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
            SetState(SearchState.Idle);
            Volatile.Write(ref _cancellation, null);
            _session.Release();
        }
    }

    private Task IgnoreTrigger(string reason)
    {
        _log.Info(nameof(SearchCoordinator), $"hotkey ignored: {reason}");
        return Task.CompletedTask;
    }

    private void SafeHideMainWindow()
    {
        try { _hideMainWindow(); }
        catch (Exception exception)
        {
            _log.Warn(nameof(SearchCoordinator), $"hiding the Flow window failed: {exception.Message}");
        }
    }

    private void SurfaceFailure(string logMessage, string userMessage, Exception exception)
    {
        _log.Error(nameof(SearchCoordinator), logMessage, exception);
        _notifier.ShowError(_strings.PluginTitle, userMessage);
    }

    private void SetState(SearchState state) => Volatile.Write(ref _state, (int)state);
}
