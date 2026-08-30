using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensWindowTests
{
    [Fact]
    public async Task Show_returns_failed_when_dispatcher_rejects_post()
    {
        var dispatcher = new TestStaDispatcher { TryPostResult = false };
        using var window = CreateWindow(dispatcher);

        var result = await window.ShowAsync([1, 2, 3], CancellationToken.None);

        Assert.Equal(GoogleLensSearchStatus.Failed, result);
        Assert.Equal(1, dispatcher.TryPostCalls);
    }

    [Fact]
    public void Repeated_dispose_sends_cleanup_and_disposes_dispatcher_once()
    {
        var dispatcher = new TestStaDispatcher();
        var window = CreateWindow(dispatcher);

        window.Dispose();
        window.Dispose();

        Assert.Equal(1, dispatcher.SendCalls);
        Assert.Equal(1, dispatcher.DisposeCalls);
    }

    [Fact]
    public void Composition_root_preserves_webview_thread_name()
    {
        Assert.Equal("CircleToSearch WebView2", CompositionRoot.GoogleLensThreadName);
    }

    private static GoogleLensWindow CreateWindow(TestStaDispatcher dispatcher)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CircleToSearch.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new GoogleLensWindow(
            AppContext.BaseDirectory,
            Path.Combine(directory, "Profile"),
            TestUiStrings.English,
            new PluginLog(directory),
            dispatcher);
    }
}
