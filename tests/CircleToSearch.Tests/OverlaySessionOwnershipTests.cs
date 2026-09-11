using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlaySessionOwnershipTests
{
    [Fact]
    public async Task Dispose_drains_unread_resource_commands()
    {
        var session = new OverlaySession(NewLog());
        var selection = NewSelection();
        session.Publish(new VisualSelection(selection, SearchProviderIds.GoogleLens));

        await session.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
    }

    [Fact]
    public void Publish_after_complete_consumes_and_disposes_payload()
    {
        var session = new OverlaySession(NewLog());
        var selection = NewSelection();
        session.Complete();

        session.Publish(new VisualSelection(selection, SearchProviderIds.GoogleLens));

        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
    }

    [Fact]
    public async Task Complete_preserves_an_already_published_command_for_the_reader()
    {
        var session = new OverlaySession(NewLog());
        var selection = NewSelection();
        session.Publish(new VisualSelection(selection, SearchProviderIds.GoogleLens));
        session.Complete();

        var command = Assert.IsType<VisualSelection>(await session.ReadCommandAsync(CancellationToken.None));

        Assert.Same(selection, command.Selection);
        Assert.NotNull(selection.FrozenFrame);
        selection.Dispose();
        await session.DisposeAsync();
    }

    private static SelectionOutcome NewSelection() =>
        new(new Rectangle(0, 0, 4, 4), new Bitmap(4, 4));

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "ownership-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }
}
