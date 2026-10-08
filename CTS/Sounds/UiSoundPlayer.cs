using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CircleToSearch.Sounds;

internal sealed class UiSoundPlayer : IDisposable
{
    internal static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
    internal const float Volume = 0.005f;
    // Keeping the device open between nearby sounds avoids a start-up delay on every click,
    // while closing it when idle stops the app from holding the audio endpoint awake.
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(10);
    // Without an audio device every click would retry and log the same failure.
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyDictionary<UiSound, float[][]> _sounds;
    private readonly Func<bool> _enabled;
    private readonly Func<(IWavePlayer Player, int SampleRate)> _createOutput;
    private readonly PluginLog? _log;
    private readonly object _gate = new();
    private readonly MixingSampleProvider _mixer = new(Format) { ReadFully = true };
    private readonly TimeProvider _time;
    private readonly ITimer _idle;
    private IWavePlayer? _output;
    private long? _openFailedAt;
    private int _mutes;
    private bool _disposed;

    internal UiSoundPlayer(IReadOnlyDictionary<UiSound, float[][]> sounds, Func<bool> enabled,
        Func<(IWavePlayer Player, int SampleRate)>? createOutput = null, TimeProvider? time = null,
        PluginLog? log = null)
    {
        _sounds = sounds;
        _enabled = enabled;
        _createOutput = createOutput ?? OpenDefaultOutput;
        _log = log;
        _time = time ?? TimeProvider.System;
        _idle = _time.CreateTimer(_ => CloseOutput(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal bool IsMuted
    {
        get { lock (_gate) return _mutes != 0; }
    }

    internal void Play(UiSound sound)
    {
        lock (_gate)
        {
            if (_disposed || _mutes != 0 || !_enabled()) return;
            if (!_sounds.TryGetValue(sound, out var variants) || variants.Length == 0) return;
            if (_output is null && _openFailedAt is { } failedAt && _time.GetElapsedTime(failedAt) < OpenRetryDelay)
                return;
            try
            {
                EnsureOutput();
                _openFailedAt = null;
            }
            catch (Exception exception)
            {
                _openFailedAt = _time.GetTimestamp();
                _log?.Warn(nameof(UiSoundPlayer), $"opening the audio output failed: {exception.Message}");
                return;
            }
            _mixer.AddMixerInput(new SampleSource(variants[Random.Shared.Next(variants.Length)]));
            _idle.Change(IdleClose, Timeout.InfiniteTimeSpan);
        }
    }

    // Loopback capture records everything the device plays, so our own sounds would leak into the recording.
    internal IDisposable Mute()
    {
        lock (_gate)
        {
            _mutes++;
            _mixer.RemoveAllMixerInputs();
        }
        return new MuteLease(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _idle.Dispose();
        CloseOutput();
    }

    private void EnsureOutput()
    {
        if (_output is not null) return;
        var (output, sampleRate) = _createOutput();
        try
        {
            // Shared mode adapts channels and bit depth but never the sample rate.
            ISampleProvider source = sampleRate == Format.SampleRate
                ? _mixer
                : new WdlResamplingSampleProvider(_mixer, sampleRate);
            output.Init(new SampleToWaveProvider(source));
            output.PlaybackStopped += OnPlaybackStopped;
            output.Play();
        }
        catch
        {
            output.Dispose();
            throw;
        }
        _output = output;
    }

    private static (IWavePlayer, int) OpenDefaultOutput()
    {
        var player = new WasapiPlayerBuilder().WithSharedMode().WithLatency(40).Build();
        return (player, player.DeviceMixFormat.SampleRate);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            _log?.Warn(nameof(UiSoundPlayer), $"audio output stopped: {e.Exception.Message}");
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output)) return;
            _output = null;
        }
        ((IWavePlayer)sender!).Dispose();
    }

    private void CloseOutput()
    {
        IWavePlayer? output;
        lock (_gate)
        {
            output = _output;
            _output = null;
            _mixer.RemoveAllMixerInputs();
        }
        if (output is null) return;
        output.PlaybackStopped -= OnPlaybackStopped;
        try
        {
            output.Stop();
            output.Dispose();
        }
        catch (Exception exception)
        {
            _log?.Warn(nameof(UiSoundPlayer), $"closing the audio output failed: {exception.Message}");
        }
    }

    private void Unmute()
    {
        lock (_gate) _mutes--;
    }

    private sealed class MuteLease(UiSoundPlayer player) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) player.Unmute();
        }
    }

    private sealed class SampleSource(float[] samples) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat => Format;

        public int Read(Span<float> buffer)
        {
            var read = Math.Min(buffer.Length, samples.Length - _position);
            for (var index = 0; index < read; index++)
                buffer[index] = samples[_position + index] * Volume;
            _position += read;
            return read;
        }
    }
}
