using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class WebView2VisualSearchBrowserSessionTests
{
    [Fact]
    public void Cleanup_failure_from_a_disposed_control_is_suppressed()
    {
        var calls = 0;

        WebView2VisualSearchBrowserSession.TryCleanup(() =>
        {
            calls++;
            throw new ObjectDisposedException("WebView2");
        });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Cleanup_still_removes_a_live_subscription()
    {
        var calls = 0;

        WebView2VisualSearchBrowserSession.TryCleanup(() => calls++);

        Assert.Equal(1, calls);
    }
}
