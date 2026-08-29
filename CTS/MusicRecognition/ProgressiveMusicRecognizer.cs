using System.Net;
using System.Net.Http;
using System.Text.Json;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Fingerprinting;
using CircleToSearch.MusicRecognition.Shazam;

namespace CircleToSearch.MusicRecognition;

public sealed class ProgressiveMusicRecognizer : IMusicRecognizer
{
    private static readonly int[] CheckpointSeconds = [4, 8, 12];
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private const double SilenceRmsThreshold = 0.0001;

    private readonly IAudioCaptureSessionFactory _captureFactory;
    private readonly IShazamClient _client;
    private readonly ShazamRequestThrottle _throttle;
    private readonly IMusicRecognitionClock _clock;
    private readonly PluginLog _log;
    private readonly TimeSpan _requestTimeout;

    public ProgressiveMusicRecognizer(
        IAudioCaptureSessionFactory captureFactory,
        IShazamClient client,
        ShazamRequestThrottle throttle,
        IMusicRecognitionClock clock,
        PluginLog log)
        : this(captureFactory, client, throttle, clock, log, RequestTimeout)
    {
    }

    internal ProgressiveMusicRecognizer(
        IAudioCaptureSessionFactory captureFactory,
        IShazamClient client,
        ShazamRequestThrottle throttle,
        IMusicRecognitionClock clock,
        PluginLog log,
        TimeSpan requestTimeout)
    {
        _captureFactory = captureFactory;
        _client = client;
        _throttle = throttle;
        _clock = clock;
        _log = log;
        _requestTimeout = requestTimeout;
    }

    public async Task<MusicRecognitionOutcome> RecognizeAsync(CancellationToken cancellationToken)
        => await RecognizeAsync(null, cancellationToken).ConfigureAwait(false);

    public async Task<MusicRecognitionOutcome> RecognizeAsync(
        IMusicVisualizationProgress? progress,
        CancellationToken cancellationToken)
    {
        if (_throttle.TryGetCooldownRemaining(out var cooldown))
        {
            _log.Info(nameof(ProgressiveMusicRecognizer), $"recognition rejected during {cooldown.TotalSeconds:0}s cooldown");
            return MusicRecognitionOutcome.From(MusicRecognitionStatus.RateLimited);
        }

        IAudioCaptureSession? capture = null;
        try
        {
            capture = _captureFactory.Create();
            var detector = new AudioTransientDetector();
            capture.Start(frame =>
            {
                if (progress is null || cancellationToken.IsCancellationRequested) return;
                try { progress.Report(detector.Process(frame)); }
                catch (Exception exception)
                {
                    _log.Warn(nameof(ProgressiveMusicRecognizer), $"reporting visualization progress failed: {exception.Message}");
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (capture is not null) await capture.DisposeAsync().ConfigureAwait(false);
            return MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled);
        }
        catch (Exception exception)
        {
            if (capture is not null) await capture.DisposeAsync().ConfigureAwait(false);
            _log.Error(nameof(ProgressiveMusicRecognizer), "starting loopback capture failed", exception);
            return MusicRecognitionOutcome.From(MusicRecognitionStatus.DeviceError);
        }

        await using var captureLease = capture;
        var captureStarted = _clock.Elapsed;
        var hadUsableFingerprint = false;
        for (var attempt = 1; attempt <= CheckpointSeconds.Length; attempt++)
        {
            try
            {
                var checkpoint = TimeSpan.FromSeconds(CheckpointSeconds[attempt - 1]);
                var remaining = checkpoint - (_clock.Elapsed - captureStarted);
                if (remaining > TimeSpan.Zero)
                    await _clock.DelayAsync(remaining, cancellationToken).ConfigureAwait(false);

                CapturedAudio snapshot;
                try
                {
                    snapshot = capture.Snapshot();
                }
                catch (Exception exception)
                {
                    _log.Error(nameof(ProgressiveMusicRecognizer), "reading loopback capture failed", exception);
                    return MusicRecognitionOutcome.From(MusicRecognitionStatus.DeviceError);
                }

                var analysis = await Task.Run(
                    () => Analyze(snapshot, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                _log.Info(
                    nameof(ProgressiveMusicRecognizer),
                    $"attempt {attempt}: {analysis.DurationSeconds:0.0}s RMS {analysis.Rms:0.0000}, {analysis.Signature.PeakCount} peaks");
                if (analysis.Rms < SilenceRmsThreshold || analysis.Signature.PeakCount == 0) continue;
                hadUsableFingerprint = true;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_requestTimeout);
                ShazamRecognition? recognition;
                try
                {
                    recognition = await _throttle.ExecuteAsync(
                        token => _client.RecognizeAsync(analysis.Signature, token),
                        timeout.Token).ConfigureAwait(false);
                }
                catch (ShazamRateLimitException exception)
                {
                    _throttle.StartCooldown(exception.RetryAfter);
                    var applied = exception.RetryAfter is { } valid && valid > TimeSpan.Zero
                        ? valid
                        : ShazamRequestThrottle.FallbackCooldown;
                    _log.Warn(
                        nameof(ProgressiveMusicRecognizer),
                        $"HTTP {(int)HttpStatusCode.TooManyRequests}; cooldown {applied.TotalSeconds:0}s; attempt {attempt}");
                    return MusicRecognitionOutcome.From(MusicRecognitionStatus.RateLimited);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _log.Warn(nameof(ProgressiveMusicRecognizer), $"request timed out on attempt {attempt}");
                    return MusicRecognitionOutcome.From(MusicRecognitionStatus.ServiceError);
                }
                catch (HttpRequestException exception)
                {
                    _log.Error(nameof(ProgressiveMusicRecognizer), $"request failed on attempt {attempt}", exception);
                    return MusicRecognitionOutcome.From(MusicRecognitionStatus.ServiceError);
                }
                catch (JsonException exception)
                {
                    _log.Error(nameof(ProgressiveMusicRecognizer), $"response parsing failed on attempt {attempt}", exception);
                    return MusicRecognitionOutcome.From(MusicRecognitionStatus.ServiceError);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (recognition is not null) return MusicRecognitionOutcome.Matched(recognition);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled);
            }
            catch (Exception exception)
            {
                _log.Error(nameof(ProgressiveMusicRecognizer), $"recognition failed on attempt {attempt}", exception);
                return MusicRecognitionOutcome.From(MusicRecognitionStatus.ServiceError);
            }
        }

        return MusicRecognitionOutcome.From(
            hadUsableFingerprint ? MusicRecognitionStatus.NoMatch : MusicRecognitionStatus.NoAudio);
    }

    private static AudioAnalysis Analyze(CapturedAudio snapshot, CancellationToken cancellationToken)
    {
        var samples = AudioPreprocessor.FromCapture(snapshot, CheckpointSeconds[^1]);
        var rms = AudioPreprocessor.CalculateRms(samples);
        cancellationToken.ThrowIfCancellationRequested();
        var signature = new ShazamSignatureGenerator().Generate(samples, cancellationToken);
        return new AudioAnalysis(
            signature,
            rms,
            samples.Length / (double)AudioPreprocessor.TargetSampleRate);
    }

    private sealed record AudioAnalysis(ShazamSignature Signature, double Rms, double DurationSeconds);
}
