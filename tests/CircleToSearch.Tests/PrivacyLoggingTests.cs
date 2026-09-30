using CircleToSearch.Capture;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class PrivacyLoggingTests
{
    [Fact]
    public void Text_command_logging_contains_only_command_metadata()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "privacy-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = new PluginLog(directory);
        var session = new OverlaySession(log);
        const string secret = "private OCR text a&b";

        session.Publish(new SearchSelectedText(secret, SearchProviderIds.GoogleLens));
        session.Complete();
        var contents = File.ReadAllText(Path.Combine(directory, "plugin.log"));

        Assert.Contains(nameof(SearchSelectedText), contents);
        Assert.DoesNotContain(secret, contents);
        Assert.DoesNotContain("google.com/search", contents);
    }

    [Fact]
    public void Link_command_logging_leaves_out_the_link()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "privacy-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = new PluginLog(directory);
        var session = new OverlaySession(log);

        session.Publish(new OpenLink(new Uri("https://example.com/reset?token=private-token")));
        session.Complete();
        var contents = File.ReadAllText(Path.Combine(directory, "plugin.log"));

        Assert.Contains(nameof(OpenLink), contents);
        Assert.DoesNotContain("private-token", contents);
        Assert.DoesNotContain("example.com", contents);
    }
}
