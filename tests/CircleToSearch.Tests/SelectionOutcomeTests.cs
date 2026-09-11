using System.Drawing;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SelectionOutcomeTests
{
    [Fact]
    public void Dispose_is_idempotent_and_access_after_dispose_is_rejected()
    {
        var bitmap = new Bitmap(8, 8);
        var selection = new SelectionOutcome(new Rectangle(1, 2, 3, 4), bitmap);

        Parallel.Invoke(selection.Dispose, selection.Dispose);

        Assert.Equal(new Rectangle(1, 2, 3, 4), selection.Bounds);
        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
        Assert.Throws<ArgumentException>(() => bitmap.GetHbitmap());
    }
}
