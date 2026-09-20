using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class UrlOpeningServiceTests
{
    private const string Url = "https://example.com/result?q=private-query";

    [Fact]
    public void Successful_open_calls_opener_once_without_notification()
    {
        var notifier = new TestPluginNotifier();
        var opened = new List<string>();
        var service = Create(url => { opened.Add(url); return true; }, notifier);

        Assert.True(service.TryOpen(Url));

        Assert.Equal([Url], opened);
        Assert.Empty(notifier.Errors);
        Assert.Empty(notifier.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Specific open failure")]
    public void False_result_reports_one_error_with_selected_message(string? failureMessage)
    {
        var notifier = new TestPluginNotifier();
        var opened = new List<string>();
        var service = Create(url => { opened.Add(url); return false; }, notifier);

        Assert.False(service.TryOpen(Url, failureMessage));

        Assert.Equal([Url], opened);
        var error = Assert.Single(notifier.Errors);
        Assert.Equal(TestUiStrings.English.PluginTitle, error.Title);
        Assert.Equal(failureMessage ?? TestUiStrings.English.ResultsUrlOpenFailed, error.Message);
    }

    [Fact]
    public void Exception_reports_one_error_and_logs_without_url_or_exception_message()
    {
        var notifier = new TestPluginNotifier();
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "url-opening-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var calls = 0;
        var service = new UrlOpeningService(_ =>
        {
            calls++;
            throw new InvalidOperationException("private-query exception detail");
        }, notifier, TestUiStrings.English, new PluginLog(directory));

        Assert.False(service.TryOpen(Url));

        Assert.Equal(1, calls);
        Assert.Equal(TestUiStrings.English.ResultsUrlOpenFailed, Assert.Single(notifier.Errors).Message);
        var log = File.ReadAllText(Path.Combine(directory, "plugin.log"));
        Assert.Contains("operation=open-url", log);
        Assert.DoesNotContain(Url, log);
        Assert.DoesNotContain("private-query exception detail", log);
    }

    [Fact]
    public void Constructor_requires_all_dependencies()
    {
        var notifier = new TestPluginNotifier();
        var log = new PluginLog(TestOutputPaths.TempDirectory);
        Assert.Throws<ArgumentNullException>(() => new UrlOpeningService(null!, notifier, TestUiStrings.English, log));
        Assert.Throws<ArgumentNullException>(() => new UrlOpeningService(_ => true, null!, TestUiStrings.English, log));
        Assert.Throws<ArgumentNullException>(() => new UrlOpeningService(_ => true, notifier, null!, log));
        Assert.Throws<ArgumentNullException>(() => new UrlOpeningService(_ => true, notifier, TestUiStrings.English, null!));
    }

    private static UrlOpeningService Create(Func<string, bool> open, TestPluginNotifier notifier) =>
        new(open, notifier, TestUiStrings.English, new PluginLog(TestOutputPaths.TempDirectory));
}
