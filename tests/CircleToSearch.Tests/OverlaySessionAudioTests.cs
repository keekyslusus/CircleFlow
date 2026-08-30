using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition.Audio;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlaySessionAudioTests
{
    [Fact]
    public void Coalescing_keeps_a_short_transient_until_the_ui_consumes_it()
    {
        var transient = new MusicVisualizationFrame(
            TimeSpan.FromMilliseconds(100), 0.8, 0.94, IsTransient: true);
        var following = new MusicVisualizationFrame(
            TimeSpan.FromMilliseconds(133), 0.68, 0.72, IsTransient: false);

        var coalesced = OverlaySession.CoalesceAudioFrames(transient, following);

        Assert.Equal(following.Elapsed, coalesced.Elapsed);
        Assert.Equal(following.NormalizedLevel, coalesced.NormalizedLevel);
        Assert.Equal(transient.NormalizedPeak, coalesced.NormalizedPeak);
        Assert.True(coalesced.IsTransient);
    }

    [Fact]
    public void Coalescing_uses_the_latest_frame_when_no_transient_is_pending()
    {
        var pending = new MusicVisualizationFrame(
            TimeSpan.FromMilliseconds(100), 0.4, 0.5, IsTransient: false);
        var latest = new MusicVisualizationFrame(
            TimeSpan.FromMilliseconds(133), 0.7, 0.8, IsTransient: false);

        Assert.Equal(latest, OverlaySession.CoalesceAudioFrames(pending, latest));
    }
}
