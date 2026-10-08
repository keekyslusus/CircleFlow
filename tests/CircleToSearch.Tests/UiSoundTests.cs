using System.IO;
using System.Windows.Controls;
using CircleToSearch.Sounds;
using Microsoft.Extensions.Time.Testing;
using NAudio.Wave;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class UiSoundTests
{
    private static readonly IReadOnlyDictionary<UiSound, float[][]> Sounds = new Dictionary<UiSound, float[][]>
    {
        [UiSound.Tap] = [[1f, 1f, 1f, 1f]],
        [UiSound.Key] = [[0.25f, 0.25f], [0.75f, 0.75f]],
    };

    [Fact]
    public void Playing_opens_one_output_and_mixes_the_sound_into_it()
    {
        var outputs = new List<FakeOutput>();
        using var player = new UiSoundPlayer(Sounds, () => true, () => Open(outputs, 48000));

        player.Play(UiSound.Tap);
        player.Play(UiSound.Tap);

        var output = Assert.Single(outputs);
        Assert.True(output.Playing);
        Assert.All(output.Read(4), sample => Assert.True(sample > 0.9f));
    }

    [Fact]
    public void Disabled_or_missing_sounds_never_open_the_output()
    {
        var outputs = new List<FakeOutput>();
        var enabled = false;
        using var player = new UiSoundPlayer(Sounds, () => enabled, () => Open(outputs, 48000));

        player.Play(UiSound.Tap);
        enabled = true;
        player.Play(UiSound.Found);

        Assert.Empty(outputs);
    }

    [Fact]
    public void Mute_silences_playing_sounds_and_blocks_new_ones_until_every_lease_is_released()
    {
        var outputs = new List<FakeOutput>();
        using var player = new UiSoundPlayer(Sounds, () => true, () => Open(outputs, 48000));
        player.Play(UiSound.Tap);
        var output = Assert.Single(outputs);

        var first = player.Mute();
        var second = player.Mute();
        Assert.All(output.Read(4), sample => Assert.Equal(0f, sample));
        player.Play(UiSound.Tap);
        first.Dispose();
        first.Dispose();
        player.Play(UiSound.Tap);
        Assert.True(player.IsMuted);
        Assert.All(output.Read(4), sample => Assert.Equal(0f, sample));

        second.Dispose();
        Assert.False(player.IsMuted);
        player.Play(UiSound.Tap);
        Assert.All(output.Read(4), sample => Assert.True(sample > 0.9f));
    }

    [Fact]
    public void Idle_output_is_closed_and_reopened_for_the_next_sound()
    {
        var outputs = new List<FakeOutput>();
        var time = new FakeTimeProvider();
        using var player = new UiSoundPlayer(Sounds, () => true, () => Open(outputs, 48000), time);

        player.Play(UiSound.Tap);
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(outputs[0].Disposed);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(outputs[0].Disposed);

        player.Play(UiSound.Tap);
        Assert.Equal(2, outputs.Count);
    }

    [Fact]
    public void Output_at_another_sample_rate_is_fed_resampled_audio()
    {
        var outputs = new List<FakeOutput>();
        using var player = new UiSoundPlayer(Sounds, () => true, () => Open(outputs, 44100));

        player.Play(UiSound.Tap);

        Assert.Equal(44100, Assert.Single(outputs).Format!.SampleRate);
    }

    [Fact]
    public void Failing_output_is_retried_only_after_a_pause()
    {
        var attempts = 0;
        var time = new FakeTimeProvider();
        using var player = new UiSoundPlayer(Sounds, () => true,
            () => ++attempts == 1 ? throw new InvalidOperationException("no device") : (new FakeOutput(), 48000), time);

        player.Play(UiSound.Tap);
        player.Play(UiSound.Tap);
        time.Advance(TimeSpan.FromSeconds(29));
        player.Play(UiSound.Tap);
        Assert.Equal(1, attempts);

        time.Advance(TimeSpan.FromSeconds(2));
        player.Play(UiSound.Tap);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Shipped_sounds_load_with_every_cue_and_four_key_variants()
    {
        var sounds = UiSoundLibrary.Load(new AppPaths(AppContext.BaseDirectory).SoundsDirectory);

        Assert.Equal(Enum.GetValues<UiSound>().Order(), sounds.Keys.Order());
        Assert.Equal(4, sounds[UiSound.Key].Length);
        Assert.All(sounds.Values.SelectMany(variants => variants), samples => Assert.NotEmpty(samples));
    }

    [Fact]
    public void Clicked_switches_sound_their_new_state_and_other_buttons_tap()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.Equal(UiSound.SwitchOn, UiClickSounds.For(new CheckBox { IsChecked = true }));
                Assert.Equal(UiSound.SwitchOff, UiClickSounds.For(new CheckBox { IsChecked = false }));
                Assert.Equal(UiSound.Tap, UiClickSounds.For(new Button()));
                Assert.Equal(UiSound.Tap, UiClickSounds.For(new RadioButton { IsChecked = true }));
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    private static (IWavePlayer, int) Open(List<FakeOutput> outputs, int sampleRate)
    {
        var output = new FakeOutput();
        outputs.Add(output);
        return (output, sampleRate);
    }

    private sealed class FakeOutput : IWavePlayer
    {
        private IWaveProvider? _source;

        public bool Playing { get; private set; }
        public bool Disposed { get; private set; }
        public WaveFormat? Format => _source?.WaveFormat;

        public float Volume { get; set; } = 1;
        public PlaybackState PlaybackState => Playing ? PlaybackState.Playing : PlaybackState.Stopped;
        public WaveFormat OutputWaveFormat => _source!.WaveFormat;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider) => _source = waveProvider;
        public void Play() => Playing = true;
        public void Pause() => Playing = false;

        public void Stop()
        {
            Playing = false;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Dispose() => Disposed = true;

        // Undoes the player's volume so assertions see the source samples.
        internal float[] Read(int count)
        {
            var bytes = new byte[count * sizeof(float)];
            _source!.Read(bytes);
            var samples = new float[count];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            return samples.Select(sample => sample / UiSoundPlayer.Volume).ToArray();
        }
    }
}

public sealed class OverlaySoundCuesTests
{
    [Fact]
    public void Qr_cue_waits_for_the_entrance_cue_to_finish()
    {
        var played = new List<UiSound>();
        var time = new FakeTimeProvider();
        using var cues = new OverlaySoundCues(played.Add, time);

        cues.Entered();
        time.Advance(TimeSpan.FromMilliseconds(100));
        cues.QrFound();
        Assert.Equal([UiSound.OverlayOpened], played);

        time.Advance(TimeSpan.FromMilliseconds(199));
        Assert.Equal([UiSound.OverlayOpened], played);
        time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Equal([UiSound.OverlayOpened, UiSound.QrFound], played);
    }

    [Fact]
    public void Late_qr_cue_plays_at_once_and_a_closed_overlay_drops_a_pending_one()
    {
        var played = new List<UiSound>();
        var time = new FakeTimeProvider();
        var cues = new OverlaySoundCues(played.Add, time);
        cues.Entered();
        time.Advance(OverlaySoundCues.EntranceGap);
        cues.QrFound();
        Assert.Equal(UiSound.QrFound, played[^1]);

        var closed = new OverlaySoundCues(played.Add, time);
        closed.Entered();
        closed.QrFound();
        closed.Dispose();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, played.Count(sound => sound == UiSound.QrFound));
        cues.Dispose();
    }

    [Fact]
    public void Trace_ticks_follow_stroke_distance_but_never_faster_than_the_minimum_interval()
    {
        var played = new List<UiSound>();
        var time = new FakeTimeProvider();
        using var cues = new OverlaySoundCues(played.Add, time);

        cues.Traced(OverlaySoundCues.TraceStepDips - 1);
        Assert.Empty(played);
        cues.Traced(2);
        Assert.Single(played);

        cues.Traced(OverlaySoundCues.TraceStepDips * 3);
        Assert.Single(played);
        time.Advance(OverlaySoundCues.TraceMinInterval);
        cues.Traced(1);
        Assert.Equal([UiSound.Trace, UiSound.Trace], played);
    }
}
