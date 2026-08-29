using System.Threading.Channels;
using System.Windows.Threading;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;

namespace CircleToSearch.Capture;

public interface IOverlaySession : IAsyncDisposable
{
    Task<IOverlayCommand> ReadCommandAsync(CancellationToken cancellationToken);
    Task ShowListeningAsync(CancellationToken cancellationToken);
    Task ReportAudioAsync(MusicVisualizationFrame frame, CancellationToken cancellationToken);
    Task ShowMusicResultAsync(MusicRecognitionOutcome outcome, CancellationToken cancellationToken);
    Task CloseAsync();
}

public interface IOverlaySessionFactory
{
    Task<IOverlaySession?> OpenAsync(OverlayLaunchOptions options, CancellationToken cancellationToken);
}

internal sealed class OverlaySession : IOverlaySession
{
    private readonly Channel<IOverlayCommand> _commands = Channel.CreateUnbounded<IOverlayCommand>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OverlayWindow? _window;
    private MusicVisualizationFrame _latestFrame;
    private readonly object _audioGate = new();
    private int _audioPostPending;
    private int _disposed;

    public void Attach(OverlayWindow window)
    {
        _window = window;
        window.Dispatcher.ShutdownFinished += (_, _) => Complete();
    }

    public void Publish(IOverlayCommand command)
    {
        if (Volatile.Read(ref _disposed) == 0) _commands.Writer.TryWrite(command);
    }

    public Task<IOverlayCommand> ReadCommandAsync(CancellationToken cancellationToken) =>
        _commands.Reader.ReadAsync(cancellationToken).AsTask();

    public Task ShowListeningAsync(CancellationToken cancellationToken) =>
        InvokeAsync(window => window.ShowListening(), cancellationToken);

    public Task ReportAudioAsync(MusicVisualizationFrame frame, CancellationToken cancellationToken)
    {
        lock (_audioGate) _latestFrame = frame;
        var window = _window;
        if (window is null || Volatile.Read(ref _disposed) != 0 ||
            Interlocked.Exchange(ref _audioPostPending, 1) != 0)
            return Task.CompletedTask;

        _ = window.Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _audioPostPending, 0);
            MusicVisualizationFrame latest;
            lock (_audioGate) latest = _latestFrame;
            if (Volatile.Read(ref _disposed) == 0) window.ReportAudio(latest);
        }, DispatcherPriority.Render, cancellationToken).Task.ContinueWith(
            _ => Interlocked.Exchange(ref _audioPostPending, 0),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return Task.CompletedTask;
    }

    public Task ShowMusicResultAsync(MusicRecognitionOutcome outcome, CancellationToken cancellationToken) =>
        InvokeAsync(window => window.ShowMusicResult(outcome), cancellationToken);

    public Task CloseAsync()
    {
        var window = _window;
        if (window is null || window.Dispatcher.HasShutdownStarted) return Task.CompletedTask;
        return window.Dispatcher.InvokeAsync(window.CloseFromSession, DispatcherPriority.Send).Task;
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
        if (Volatile.Read(ref _disposed) != 0 || window.Dispatcher.HasShutdownStarted)
            return Task.CompletedTask;
        return window.Dispatcher.InvokeAsync(() => action(window), DispatcherPriority.Normal, cancellationToken).Task;
    }

    private void Complete()
    {
        _commands.Writer.TryComplete();
        _closed.TrySetResult();
    }
}
