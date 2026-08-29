using System.IO;
using NAudio.Wave;

namespace CircleToSearch.MusicRecognition.Audio;

public interface IAudioCaptureSession : IAsyncDisposable
{
    void Start(CancellationToken cancellationToken);
    void Start(Action<AudioLevelFrame>? progress, CancellationToken cancellationToken) => Start(cancellationToken);
    CapturedAudio Snapshot();
}

public interface IAudioCaptureSessionFactory
{
    IAudioCaptureSession Create();
}

public sealed class LoopbackCaptureSessionFactory : IAudioCaptureSessionFactory
{
    private readonly PluginLog? _log;

    public LoopbackCaptureSessionFactory(PluginLog? log = null) => _log = log;

    public IAudioCaptureSession Create() => new LoopbackCaptureSession(_log);
}

public sealed class LoopbackCaptureSession : IAudioCaptureSession
{
    private const int MaximumCaptureSeconds = 12;
    private readonly object _sync = new();
    private readonly WasapiRecorder _recorder;
    private readonly MemoryStream _buffer;
    private readonly int _maximumBytes;
    private readonly PluginLog? _log;
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private int _disposed;
    private long _capturedBytes;
    private TimeSpan _lastProgress;

    public LoopbackCaptureSession(PluginLog? log = null)
    {
        _log = log;
        _recorder = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
        _maximumBytes = checked(_recorder.WaveFormat.AverageBytesPerSecond * MaximumCaptureSeconds);
        _buffer = new MemoryStream(_maximumBytes);
    }

    public void Start(CancellationToken cancellationToken)
        => Start(null, cancellationToken);

    public void Start(Action<AudioLevelFrame>? progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_captureTask is not null) throw new InvalidOperationException("Capture has already started.");
        _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureTask = CaptureAsync(progress, _captureCancellation.Token);
    }

    public CapturedAudio Snapshot()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_captureTask is null) throw new InvalidOperationException("Capture has not started.");
        if (_captureTask.IsFaulted)
            throw _captureTask.Exception?.InnerException ?? new InvalidOperationException("Audio capture failed.");

        lock (_sync)
            return new CapturedAudio(_buffer.ToArray(), _recorder.WaveFormat);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _captureCancellation?.Cancel();
        if (_captureTask is not null)
        {
            try
            {
                await _captureTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_captureCancellation?.IsCancellationRequested == true)
            {
            }
        }
        _captureCancellation?.Dispose();
        await _recorder.DisposeAsync().ConfigureAwait(false);
        _buffer.Dispose();
    }

    private async Task CaptureAsync(Action<AudioLevelFrame>? progress, CancellationToken cancellationToken)
    {
        await foreach (var audioBuffer in _recorder.CaptureAsync(cancellationToken).ConfigureAwait(false))
        {
            var data = audioBuffer.Data.Span;
            var elapsed = TimeSpan.FromSeconds(
                Interlocked.Add(ref _capturedBytes, data.Length) /
                (double)_recorder.WaveFormat.AverageBytesPerSecond);
            AudioLevelFrame? level = null;
            if (progress is not null && elapsed - _lastProgress >= TimeSpan.FromMilliseconds(33))
            {
                _lastProgress = elapsed;
                level = AudioLevelMeter.Measure(data, _recorder.WaveFormat, elapsed);
            }
            var reachedLimit = false;
            lock (_sync)
            {
                var remaining = _maximumBytes - (int)_buffer.Length;
                if (remaining <= 0) continue;
                _buffer.Write(data[..Math.Min(remaining, data.Length)]);
                reachedLimit = _buffer.Length >= _maximumBytes;
            }
            if (level is { } report)
            {
                try { progress!(report); }
                catch (Exception exception)
                {
                    _log?.Warn(nameof(LoopbackCaptureSession),
                        $"reporting audio visualization progress failed: {exception.Message}");
                }
            }
            if (reachedLimit) _captureCancellation?.Cancel();
        }
    }
}
