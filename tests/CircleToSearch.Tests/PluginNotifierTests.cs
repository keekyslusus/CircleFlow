using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class PluginNotifierTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Host_notification_exception_is_suppressed(int operation)
    {
        var notifier = new PluginNotifier(
            (_, _) => throw new InvalidOperationException("message failed"),
            (_, _, _, _) => throw new InvalidOperationException("button failed"),
            (_, _) => throw new InvalidOperationException("error failed"),
            NewLog());

        var exception = Record.Exception(() =>
        {
            if (operation == 0) notifier.ShowMessage("title", "message");
            else if (operation == 1) notifier.ShowMessageWithButton("title", "message", "button", () => { });
            else notifier.ShowError("title", "message");
        });

        Assert.Null(exception);
    }

    private static PluginLog NewLog()
    {
        var path = Path.Combine(TestOutputPaths.TempDirectory, "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new PluginLog(path);
    }
}
