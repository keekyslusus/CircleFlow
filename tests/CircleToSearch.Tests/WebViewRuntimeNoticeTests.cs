using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class WebViewRuntimeNoticeTests
{
    [Fact]
    public void Missing_runtime_offers_a_download_that_opens_the_runtime_page()
    {
        var harness = new Harness(runtimeVersion: null);

        harness.Notice.ShowIfMissing();

        var offer = Assert.Single(harness.Notifier.Buttons);
        Assert.Equal(TestUiStrings.English.WebViewRuntimeMissingTitle, offer.Title);
        Assert.Equal(TestUiStrings.English.WebViewRuntimeMissing, offer.Message);
        Assert.Equal(TestUiStrings.English.WebViewRuntimeDownload, offer.Button);
        Assert.Empty(harness.Opened);
        offer.Action();
        Assert.Equal([WebViewRuntimeNotice.DownloadUrl], harness.Opened);
    }

    [Fact]
    public void Installed_runtime_shows_nothing()
    {
        var harness = new Harness(runtimeVersion: "130.0.2849.80");

        harness.Notice.ShowIfMissing();

        Assert.Empty(harness.Notifier.Buttons);
        Assert.Empty(harness.Notifier.Messages);
        Assert.Empty(harness.Notifier.Errors);
    }

    private sealed class Harness
    {
        public Harness(string? runtimeVersion)
        {
            var directory = Path.Combine(TestOutputPaths.TempDirectory, "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var urlOpening = new UrlOpeningService(url => { Opened.Add(url); return true; },
                Notifier, TestUiStrings.English, new PluginLog(directory));
            Notice = new WebViewRuntimeNotice(() => runtimeVersion, Notifier, urlOpening, TestUiStrings.English);
        }

        public TestPluginNotifier Notifier { get; } = new();
        public List<string> Opened { get; } = [];
        public WebViewRuntimeNotice Notice { get; }
    }
}
