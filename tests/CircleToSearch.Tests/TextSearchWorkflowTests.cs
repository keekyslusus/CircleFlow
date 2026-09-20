using CircleToSearch.Search;
using CircleToSearch.Shell;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TextSearchWorkflowTests
{
    [Fact]
    public void Opens_validated_url_and_reports_opener_failure()
    {
        var opened = new List<string>();
        var errors = new List<string>();
        var workflow = CreateWorkflow(url => { opened.Add(url); return false; }, errors, 2000);

        var success = workflow.Execute("a&b", SearchProviderIds.GoogleLens);

        Assert.False(success);
        Assert.Contains("q=a%26b", Assert.Single(opened));
        Assert.Equal([TestUiStrings.English.TextSearchOpenFailed], errors);
    }

    [Fact]
    public void Oversized_query_is_rejected_before_browser_open()
    {
        var openCalls = 0;
        var errors = new List<string>();
        var workflow = CreateWorkflow(_ => { openCalls++; return true; }, errors, 2);

        var success = workflow.Execute("abc", SearchProviderIds.GoogleLens);

        Assert.False(success);
        Assert.Equal(0, openCalls);
        Assert.Equal([TestUiStrings.English.TextSearchTooLong], errors);
    }

    private static TextSearchWorkflow CreateWorkflow(
        Func<string, bool> open,
        List<string> errors,
        int maximumScalars)
    {
        var logDirectory = Path.Combine(TestOutputPaths.TempDirectory, "text-search-logs");
        Directory.CreateDirectory(logDirectory);
        var notifier = new Notifier(errors);
        var log = new PluginLog(logDirectory);
        return new TextSearchWorkflow(
            new TextSearchUrlBuilder(maximumScalars),
            new UrlOpeningService(open, notifier, TestUiStrings.English, log),
            notifier,
            TestUiStrings.English,
            log);
    }

    private sealed class Notifier(List<string> errors) : IPluginNotifier
    {
        public void ShowMessage(string title, string message) { }
        public void ShowMessageWithButton(string title, string message, string button, Action action) { }
        public void ShowError(string title, string message) => errors.Add(message);
    }
}
