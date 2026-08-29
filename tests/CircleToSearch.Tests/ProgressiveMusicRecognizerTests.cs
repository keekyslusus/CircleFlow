using System.Net;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Fingerprinting;
using CircleToSearch.MusicRecognition.Shazam;
using NAudio.Wave;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ProgressiveMusicRecognizerTests
{
    [Fact]
    public async Task Match_at_four_seconds_stops_after_one_request()
    {
        using var harness = new Harness();
        harness.Client.Results.Enqueue(new ShazamRecognition("Title", "Artist", null, null, null, null, null));

        var outcome = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.Matched, outcome.Status);
        Assert.Equal([TimeSpan.FromSeconds(4)], harness.Client.RequestStarts);
        Assert.Equal(1, harness.Capture.SnapshotCalls);
        Assert.Equal(1, harness.Capture.DisposeCalls);
    }

    [Fact]
    public async Task No_match_then_match_uses_fresh_eight_second_snapshot()
    {
        using var harness = new Harness();
        harness.Client.Results.Enqueue(null);
        harness.Client.Results.Enqueue(new ShazamRecognition("Title", "Artist", null, null, null, null, null));

        var outcome = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.Matched, outcome.Status);
        Assert.Equal([TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)], harness.Client.RequestStarts);
        Assert.True(harness.Client.SampleCounts[1] > harness.Client.SampleCounts[0]);
    }

    [Fact]
    public async Task Three_no_matches_end_at_twelve_seconds_with_serial_requests()
    {
        using var harness = new Harness();
        harness.Client.Results.Enqueue(null);
        harness.Client.Results.Enqueue(null);
        harness.Client.Results.Enqueue(null);

        var outcome = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.NoMatch, outcome.Status);
        Assert.Equal(
            [TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12)],
            harness.Client.RequestStarts);
        Assert.All(
            harness.Client.RequestStarts.Zip(harness.Client.RequestStarts.Skip(1)),
            pair => Assert.True(pair.Second - pair.First >= TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public async Task Visualization_progress_preserves_four_eight_twelve_schedule()
    {
        using var harness = new Harness();
        harness.Client.Results.Enqueue(null);
        harness.Client.Results.Enqueue(null);
        harness.Client.Results.Enqueue(null);
        var progress = new RecordingProgress();

        var outcome = await harness.Recognizer.RecognizeAsync(progress, CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.NoMatch, outcome.Status);
        Assert.Equal(
            [TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12)],
            harness.Client.RequestStarts);
        Assert.NotEmpty(progress.Frames);
    }

    [Fact]
    public async Task Silent_snapshots_skip_the_network_and_return_no_audio()
    {
        using var harness = new Harness(silent: true);

        var outcome = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.NoAudio, outcome.Status);
        Assert.Empty(harness.Client.RequestStarts);
        Assert.Equal(3, harness.Capture.SnapshotCalls);
    }

    [Fact]
    public async Task Late_response_uses_a_longer_snapshot_without_parallel_request()
    {
        using var harness = new Harness();
        harness.Client.Results.Enqueue(null);
        harness.Client.Results.Enqueue(new ShazamRecognition("Title", "Artist", null, null, null, null, null));
        harness.Client.AdvanceAfterRequest = TimeSpan.FromSeconds(5);

        var outcome = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.Matched, outcome.Status);
        Assert.Equal([TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(9)], harness.Client.RequestStarts);
        Assert.True(harness.Client.SampleCounts[1] > harness.Client.SampleCounts[0]);
        Assert.Equal(1, harness.Client.MaximumConcurrency);
    }

    [Fact]
    public async Task Transport_failure_stops_without_another_attempt()
    {
        using var harness = new Harness();
        harness.Client.Exception = new HttpRequestException("offline");

        var outcome = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.ServiceError, outcome.Status);
        Assert.Single(harness.Client.RequestStarts);
        Assert.Equal(1, harness.Capture.DisposeCalls);
    }

    [Fact]
    public async Task Device_start_failure_disposes_the_partially_started_session()
    {
        using var harness = new Harness();
        harness.Capture.ThrowOnStart = true;

        var outcome = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.DeviceError, outcome.Status);
        Assert.Empty(harness.Client.RequestStarts);
        Assert.Equal(1, harness.Capture.DisposeCalls);
    }

    [Fact]
    public async Task Request_timeout_is_a_service_failure_without_retry()
    {
        using var harness = new Harness(requestTimeout: TimeSpan.FromMilliseconds(10));
        harness.Client.WaitForCancellation = true;

        var outcome = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.ServiceError, outcome.Status);
        Assert.Single(harness.Client.RequestStarts);
        Assert.Equal(1, harness.Capture.DisposeCalls);
    }

    [Fact]
    public async Task Rate_limit_stops_and_cooldown_rejects_before_capture()
    {
        using var harness = new Harness();
        harness.Client.Exception = new ShazamRateLimitException(TimeSpan.FromSeconds(30));

        var first = await harness.Recognizer.RecognizeAsync(CancellationToken.None);
        var second = await harness.Recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.RateLimited, first.Status);
        Assert.Equal(MusicRecognitionStatus.RateLimited, second.Status);
        Assert.Single(harness.Client.RequestStarts);
        Assert.Equal(1, harness.Factory.CreateCalls);
    }

    [Fact]
    public void Missing_retry_after_uses_sixty_second_cooldown()
    {
        using var harness = new Harness();

        harness.Throttle.StartCooldown(null);

        Assert.True(harness.Throttle.TryGetCooldownRemaining(out var remaining));
        Assert.Equal(TimeSpan.FromSeconds(60), remaining);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.False(harness.Throttle.TryGetCooldownRemaining(out _));
    }

    [Fact]
    public async Task Cancellation_during_checkpoint_delay_disposes_capture_and_sends_nothing()
    {
        using var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await harness.Recognizer.RecognizeAsync(cancellation.Token);

        Assert.Equal(MusicRecognitionStatus.Canceled, outcome.Status);
        Assert.Empty(harness.Client.RequestStarts);
        Assert.Equal(1, harness.Capture.DisposeCalls);
    }

    [Fact]
    public async Task Cancellation_during_noncooperative_request_suppresses_late_match()
    {
        using var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.Client.Results.Enqueue(new ShazamRecognition("Title", "Artist", null, null, null, null, null));
        harness.Client.OnRequest = cancellation.Cancel;

        var outcome = await harness.Recognizer.RecognizeAsync(cancellation.Token);

        Assert.Equal(MusicRecognitionStatus.Canceled, outcome.Status);
        Assert.Single(harness.Client.RequestStarts);
        Assert.Equal(1, harness.Capture.DisposeCalls);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _logDirectory;

        public Harness(bool silent = false, TimeSpan? requestTimeout = null)
        {
            _logDirectory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_logDirectory);
            Clock = new FakeClock();
            Capture = new FakeCapture(Clock, silent);
            Factory = new FakeCaptureFactory(Capture);
            Client = new FakeClient(Clock);
            Throttle = new ShazamRequestThrottle(Clock);
            var log = new PluginLog(_logDirectory);
            Recognizer = requestTimeout is null
                ? new ProgressiveMusicRecognizer(Factory, Client, Throttle, Clock, log)
                : new ProgressiveMusicRecognizer(Factory, Client, Throttle, Clock, log, requestTimeout.Value);
        }

        public FakeClock Clock { get; }
        public FakeCapture Capture { get; }
        public FakeCaptureFactory Factory { get; }
        public FakeClient Client { get; }
        public ShazamRequestThrottle Throttle { get; }
        public ProgressiveMusicRecognizer Recognizer { get; }

        public void Dispose() => Throttle.Dispose();
    }

    private sealed class FakeClock : IMusicRecognitionClock
    {
        public TimeSpan Elapsed { get; private set; }
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch + Elapsed;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Elapsed += delay;
            return Task.CompletedTask;
        }

        public void Advance(TimeSpan duration) => Elapsed += duration;
    }

    private sealed class FakeCaptureFactory(FakeCapture capture) : IAudioCaptureSessionFactory
    {
        public int CreateCalls { get; private set; }

        public IAudioCaptureSession Create()
        {
            CreateCalls++;
            return capture;
        }
    }

    private sealed class FakeCapture(FakeClock clock, bool silent) : IAudioCaptureSession
    {
        private readonly float[] _samples = CreateSamples(silent);
        public int SnapshotCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool ThrowOnStart { get; set; }

        public void Start(CancellationToken cancellationToken)
        {
            if (ThrowOnStart) throw new InvalidOperationException("device unavailable");
        }

        public void Start(Action<AudioLevelFrame>? progress, CancellationToken cancellationToken)
        {
            Start(cancellationToken);
            progress?.Invoke(new AudioLevelFrame(TimeSpan.Zero, 0, 0));
            progress?.Invoke(new AudioLevelFrame(TimeSpan.FromMilliseconds(100), 0.05, 0.1));
        }

        public CapturedAudio Snapshot()
        {
            SnapshotCalls++;
            var sampleCount = Math.Min(_samples.Length, (int)(clock.Elapsed.TotalSeconds * 16_000));
            var bytes = new byte[sampleCount * sizeof(float)];
            Buffer.BlockCopy(_samples, 0, bytes, 0, bytes.Length);
            return new CapturedAudio(bytes, WaveFormat.CreateIeeeFloatWaveFormat(16_000, 1));
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }

        private static float[] CreateSamples(bool silent)
        {
            var samples = new float[16_000 * 12];
            if (silent) return samples;
            double[] frequencies = [330, 660, 1320, 3600];
            for (var index = 0; index < samples.Length; index++)
            {
                var time = index / 16_000d;
                var pulse = Math.Sin(Math.PI * ((time % 0.35) / 0.35));
                samples[index] = (float)(pulse * frequencies.Sum(f => Math.Sin(2 * Math.PI * f * time)) / 5);
            }
            return samples;
        }
    }

    private sealed class RecordingProgress : IMusicVisualizationProgress
    {
        public List<MusicVisualizationFrame> Frames { get; } = [];
        public void Report(MusicVisualizationFrame frame) => Frames.Add(frame);
    }

    private sealed class FakeClient(FakeClock clock) : IShazamClient
    {
        private int _concurrency;
        public Queue<ShazamRecognition?> Results { get; } = new();
        public Exception? Exception { get; set; }
        public TimeSpan AdvanceAfterRequest { get; set; }
        public bool WaitForCancellation { get; set; }
        public Action? OnRequest { get; set; }
        public List<TimeSpan> RequestStarts { get; } = [];
        public List<int> SampleCounts { get; } = [];
        public int MaximumConcurrency { get; private set; }

        public async Task<ShazamRecognition?> RecognizeAsync(
            ShazamSignature signature,
            CancellationToken cancellationToken)
        {
            RequestStarts.Add(clock.Elapsed);
            SampleCounts.Add(signature.NumberSamples);
            MaximumConcurrency = Math.Max(MaximumConcurrency, Interlocked.Increment(ref _concurrency));
            try
            {
                if (Exception is not null) throw Exception;
                OnRequest?.Invoke();
                if (WaitForCancellation)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                if (AdvanceAfterRequest > TimeSpan.Zero) clock.Advance(AdvanceAfterRequest);
                return Results.Count == 0 ? null : Results.Dequeue();
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }
    }
}
