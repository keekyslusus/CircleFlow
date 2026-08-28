using System.IO;
using NAudio.Wave;

namespace CircleToSearch.MusicRecognition.Audio;

public interface IAudioCaptureSession : IAsyncDisposable
{
    void Start(CancellationToken cancellationToken);
    CapturedAudio Snapshot();
}

public interface IAudioCaptureSessionFactory
{
    IAudioCaptureSession Create();
}

public sealed class LoopbackCaptureSessionFactory : IAudioCaptureSessionFactory
{
    public IAudioCaptureSession Create() => new LoopbackCaptureSession();
}

public sealed class LoopbackCaptureSession : IAudioCaptureSession
{
    private const int MaximumCaptureSeconds = 12;
    private readonly object _sync = new();
    private readonly WasapiRecorder _recorder;
    private readonly MemoryStream _buffer;
    private readonly int _maximumBytes;
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private int _disposed;

    public LoopbackCaptureSession()
    {
        _recorder = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
        _maximumBytes = checked(_recorder.WaveFormat.AverageBytesPerSecond * MaximumCaptureSeconds);
        _buffer = new MemoryStream(_maximumBytes);
    }

    public void Start(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_captureTask is not null) throw new InvalidOperationException("Capture has already started.");
        _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureTask = CaptureAsync(_captureCancellation.Token);
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

    private async Task CaptureAsync(CancellationToken cancellationToken)
    {
        await foreach (var audioBuffer in _recorder.CaptureAsync(cancellationToken).ConfigureAwait(false))
        {
            var reachedLimit = false;
            lock (_sync)
            {
                var remaining = _maximumBytes - (int)_buffer.Length;
                if (remaining <= 0) continue;
                var data = audioBuffer.Data.Span;
                _buffer.Write(data[..Math.Min(remaining, data.Length)]);
                reachedLimit = _buffer.Length >= _maximumBytes;
            }
            if (reachedLimit) _captureCancellation?.Cancel();
        }
    }
}
