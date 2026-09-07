using System.Threading.Channels;
using System.Windows.Threading;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Translation;

namespace CircleToSearch.Capture;

public interface IOverlaySession : IAsyncDisposable
{
    Task<IOverlayCommand> ReadCommandAsync(CancellationToken cancellationToken);
    Task ShowListeningAsync(CancellationToken cancellationToken);
    Task ShowTraceResultAsync(Search.VisualSearchPreparationOutcome outcome, CancellationToken cancellationToken) => Task.CompletedTask;
    Task ReportAudioAsync(MusicVisualizationFrame frame, CancellationToken cancellationToken);
    Task ShowMusicResultAsync(MusicRecognitionOutcome outcome, CancellationToken cancellationToken);
    Task ShowTranslationAsync(ScreenTranslationResult result, CancellationToken cancellationToken) => Task.CompletedTask;
    Task ShowTranslationFailureAsync(Guid requestId, TranslationFailure failure, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    Task CloseAsync();
    Task WaitForCloseAsync(CancellationToken cancellationToken);
}

public interface IOverlaySessionFactory
{
    Task<IOverlaySession?> OpenAsync(OverlayLaunchOptions options, CancellationToken cancellationToken);
}

internal sealed class OverlaySession : IOverlaySession
{
    private readonly Channel<IOverlayCommand> _commands = Channel.CreateUnbounded<IOverlayCommand>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OverlayWindow? _window;
    private MusicVisualizationFrame _latestFrame;
    private readonly object _audioGate = new();
    private int _audioPostPending;
    private int _disposed;

    private readonly PluginLog _log;

    public OverlaySession(PluginLog log) => _log = log;

    public void Attach(OverlayWindow window)
    {
        _window = window;
    }

    public void Publish(IOverlayCommand command)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_commands.Writer.TryWrite(command))
            _log.Info(nameof(OverlaySession), $"command '{command.GetType().Name}' published");
    }

    public Task<IOverlayCommand> ReadCommandAsync(CancellationToken cancellationToken) =>
        _commands.Reader.ReadAsync(cancellationToken).AsTask();

    public Task ShowTraceResultAsync(Search.VisualSearchPreparationOutcome outcome, CancellationToken cancellationToken) =>
        InvokeAsync(window => window.ShowTraceResult(outcome), cancellationToken);

    public Task ShowListeningAsync(CancellationToken cancellationToken) =>
        InvokeAsync(window => window.ShowListening(), cancellationToken);

    public Task ReportAudioAsync(MusicVisualizationFrame frame, CancellationToken cancellationToken)
    {
        var window = _window;
        if (window is null || Volatile.Read(ref _disposed) != 0)
            return Task.CompletedTask;

        lock (_audioGate)
        {
            if (_audioPostPending != 0)
            {
                _latestFrame = CoalesceAudioFrames(_latestFrame, frame);
                return Task.CompletedTask;
            }

            _latestFrame = frame;
            _audioPostPending = 1;
        }

        _ = window.Dispatcher.InvokeAsync(() =>
        {
            MusicVisualizationFrame latest;
            lock (_audioGate)
            {
                latest = _latestFrame;
                _audioPostPending = 0;
            }
            if (Volatile.Read(ref _disposed) == 0) window.ReportAudio(latest);
        }, DispatcherPriority.Render, cancellationToken).Task.ContinueWith(
            task =>
            {
                if (task.IsCompletedSuccessfully) return;
                lock (_audioGate) _audioPostPending = 0;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return Task.CompletedTask;
    }

    internal static MusicVisualizationFrame CoalesceAudioFrames(
        MusicVisualizationFrame pending,
        MusicVisualizationFrame latest) =>
        latest with
        {
            NormalizedPeak = pending.IsTransient
                ? Math.Max(pending.NormalizedPeak, latest.NormalizedPeak)
                : latest.NormalizedPeak,
            IsTransient = pending.IsTransient || latest.IsTransient,
        };

    public Task ShowMusicResultAsync(MusicRecognitionOutcome outcome, CancellationToken cancellationToken) =>
        InvokeAsync(window => window.ShowMusicResult(outcome), cancellationToken);

    public Task ShowTranslationAsync(ScreenTranslationResult result, CancellationToken cancellationToken) =>
        InvokeAsync(window => window.ShowTranslation(result), cancellationToken);

    public Task ShowTranslationFailureAsync(
        Guid requestId,
        TranslationFailure failure,
        CancellationToken cancellationToken) =>
        InvokeAsync(window => window.ShowTranslationFailure(requestId, failure), cancellationToken);

    public Task WaitForCloseAsync(CancellationToken cancellationToken) =>
        _closed.Task.WaitAsync(cancellationToken);

    public async Task CloseAsync()
    {
        var window = _window;
        if (window is null || _closed.Task.IsCompleted) return;
        var dispatcher = window.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            await _closed.Task.ConfigureAwait(false);
            return;
        }
        await InvokeWhileDispatcherAliveAsync(
            dispatcher,
            window.CloseFromSession,
            DispatcherPriority.Send,
            CancellationToken.None).ConfigureAwait(false);
        await _closed.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await CloseAsync().ConfigureAwait(false); }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) when (_window?.Dispatcher.HasShutdownStarted == true) { }
        if (_window is not null) await _closed.Task.ConfigureAwait(false);
        Complete();
    }

    private Task InvokeAsync(Action<OverlayWindow> action, CancellationToken cancellationToken)
    {
        var window = _window ?? throw new InvalidOperationException("The overlay session is not ready.");
        if (Volatile.Read(ref _disposed) != 0 ||
            _closed.Task.IsCompleted ||
            window.Dispatcher.HasShutdownStarted ||
            window.Dispatcher.HasShutdownFinished)
            return Task.CompletedTask;
        return InvokeWhileDispatcherAliveAsync(
            window.Dispatcher,
            () => action(window),
            DispatcherPriority.Normal,
            cancellationToken);
    }

    private async Task InvokeWhileDispatcherAliveAsync(
        Dispatcher dispatcher,
        Action action,
        DispatcherPriority priority,
        CancellationToken cancellationToken)
    {
        DispatcherOperation operation;
        try
        {
            operation = dispatcher.InvokeAsync(action, priority, cancellationToken);
        }
        catch (InvalidOperationException) when (
            _closed.Task.IsCompleted || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        await Task.WhenAny(operation.Task, _closed.Task).ConfigureAwait(false);
        if (operation.Task.IsCompleted)
        {
            await operation.Task.ConfigureAwait(false);
            return;
        }

        _ = operation.Task.ContinueWith(
            task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal void Complete(Exception? exception = null)
    {
        var channelCompleted = _commands.Writer.TryComplete(exception);
        _closed.TrySetResult();
        if (channelCompleted)
            _log.Info(nameof(OverlaySession), exception is null
                ? "command channel completed"
                : $"command channel faulted: {exception.GetType().Name}");
    }
}
