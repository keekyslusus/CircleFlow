using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Search;
using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class MusicResultPresenterTests
{
    [Theory]
    [InlineData(MusicRecognitionStatus.NoMatch, false, "matching track")]
    [InlineData(MusicRecognitionStatus.NoAudio, false, "default Windows output")]
    [InlineData(MusicRecognitionStatus.RateLimited, true, "rate-limited")]
    [InlineData(MusicRecognitionStatus.ServiceError, true, "could not be reached")]
    [InlineData(MusicRecognitionStatus.DeviceError, true, "could not be captured")]
    public void Status_maps_to_existing_flow_message(
        MusicRecognitionStatus status,
        bool error,
        string expected)
    {
        var notifier = new TestPluginNotifier();
        var presenter = new MusicResultPresenter(Opening(_ => true, notifier), notifier, TestUiStrings.English);

        presenter.PresentFallback(MusicRecognitionOutcome.From(status));

        var message = error
            ? Assert.Single(notifier.Errors).Message
            : Assert.Single(notifier.Messages).Message;
        Assert.Contains(expected, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Safe_match_shows_button_and_opens_only_when_action_runs()
    {
        var opened = new List<string>();
        var notifier = new TestPluginNotifier();
        var presenter = new MusicResultPresenter(
            Opening(url => { opened.Add(url); return true; }, notifier), notifier, TestUiStrings.English);
        var match = Match("https://www.shazam.com/track/1");

        presenter.PresentFallback(MusicRecognitionOutcome.Matched(match));

        var button = Assert.Single(notifier.Buttons);
        Assert.Equal("Artist - Track", button.Title);
        Assert.Empty(opened);
        button.Action();
        Assert.Equal([match.ShazamUrl], opened);
    }

    [Fact]
    public void Unsafe_match_shows_plain_message()
    {
        var notifier = new TestPluginNotifier();
        var presenter = new MusicResultPresenter(Opening(_ => true, notifier), notifier, TestUiStrings.English);

        presenter.PresentFallback(MusicRecognitionOutcome.Matched(Match("https://example.com/track/1")));

        Assert.Empty(notifier.Buttons);
        Assert.Single(notifier.Messages);
    }

    [Theory]
    [InlineData("https://shazam.com/track/1", true)]
    [InlineData("https://WWW.SHAZAM.COM/track/1", true)]
    [InlineData("http://www.shazam.com/track/1", false)]
    [InlineData("https://example.com/track/1", false)]
    [InlineData("https://shazam.com.evil.example/track/1", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void Shazam_url_policy_is_strict(string? url, bool expected)
    {
        Assert.Equal(expected, MusicResultPresenter.IsSafeShazamUrl(url));
    }

    [Fact]
    public void Opener_failure_surfaces_existing_error()
    {
        var notifier = new TestPluginNotifier();
        var presenter = new MusicResultPresenter(Opening(_ => false, notifier), notifier, TestUiStrings.English);

        presenter.Open(Match("https://www.shazam.com/track/1"));

        Assert.Contains("open", Assert.Single(notifier.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Canceled_outcome_has_no_ui_side_effects()
    {
        var notifier = new TestPluginNotifier();
        var presenter = new MusicResultPresenter(Opening(_ => true, notifier), notifier, TestUiStrings.English);

        presenter.PresentFallback(MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled));

        Assert.Empty(notifier.Messages);
        Assert.Empty(notifier.Buttons);
        Assert.Empty(notifier.Errors);
    }

    private static ShazamRecognition Match(string? url) =>
        new("Track", "Artist", "Album", "Genre", null, null, url);

    private static UrlOpeningService Opening(Func<string, bool> open, TestPluginNotifier notifier) =>
        new(open, notifier, TestUiStrings.English, new PluginLog(TestOutputPaths.TempDirectory));
}
