using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchBrowserHostTests
{
    [Fact]
    public async Task Show_returns_initialization_failed_when_dispatcher_rejects_post()
    {
        var dispatcher = new TestStaDispatcher { TryPostResult = false };
        using var host = CreateHost(dispatcher);

        var result = await host.ShowAsync(
            new SearchProviderDescriptor("test", "Test"),
            PreparedVisualSearch.ForUrl(new Uri("https://example.com"), null),
            CancellationToken.None);

        Assert.Equal(SearchBrowserShowStatus.InitializationFailed, result.Status);
        Assert.Equal(1, dispatcher.TryPostCalls);
    }

    [Fact]
    public async Task Disposed_host_rejects_new_shows_as_canceled()
    {
        var dispatcher = new TestStaDispatcher();
        var host = CreateHost(dispatcher);
        host.Dispose();

        var result = await host.ShowAsync(
            new SearchProviderDescriptor("test", "Test"),
            PreparedVisualSearch.ForUrl(new Uri("https://example.com"), null),
            CancellationToken.None);

        Assert.Equal(SearchBrowserShowStatus.Canceled, result.Status);
    }

    [Fact]
    public void Repeated_dispose_sends_cleanup_and_disposes_dispatcher_once()
    {
        var dispatcher = new TestStaDispatcher();
        var host = CreateHost(dispatcher);

        host.Dispose();
        host.Dispose();

        Assert.Equal(1, dispatcher.StopCalls);
        Assert.Equal(1, dispatcher.DisposeCalls);
    }

    [Fact]
    public async Task Dispose_completes_a_posted_show_as_canceled()
    {
        var dispatcher = new TestStaDispatcher();
        var host = CreateHost(dispatcher);
        Task<SearchBrowserShowResult>? show = null;
        dispatcher.BeforeSendAction = () => Assert.False(show!.IsCompleted);
        show = host.ShowAsync(
            new SearchProviderDescriptor("test", "Test"),
            PreparedVisualSearch.ForUrl(new Uri("https://example.com"), null),
            CancellationToken.None);
        Assert.False(show.IsCompleted);

        host.Dispose();

        Assert.Equal(SearchBrowserShowStatus.Canceled, (await show).Status);
    }

    [Fact]
    public async Task Dispose_returns_after_timeout_and_defers_dispatcher_cleanup_when_initialization_never_completes()
    {
        var initialization = new TaskCompletionSource<CoreWebView2Environment>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new TestStaDispatcher { ExecutePostedAction = true };
        var host = CreateHost(
            dispatcher,
            () => initialization.Task,
            TimeSpan.FromMilliseconds(25));
        var show = host.ShowAsync(
            new SearchProviderDescriptor("test", "Test"),
            PreparedVisualSearch.ForUrl(new Uri("https://example.com"), null),
            CancellationToken.None);
        Assert.False(show.IsCompleted);

        var elapsed = Stopwatch.StartNew();
        host.Dispose();
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(0, dispatcher.DisposeCalls);
        Assert.False(show.IsCompleted);

        initialization.SetException(new OperationCanceledException());

        Assert.Equal(SearchBrowserShowStatus.Canceled, (await show).Status);
        Assert.True(SpinWait.SpinUntil(() => dispatcher.DisposeCalls == 1, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Concurrent_show_waits_at_gate_and_can_be_canceled()
    {
        var dispatcher = new TestStaDispatcher();
        var host = CreateHost(dispatcher);
        var descriptor = new SearchProviderDescriptor("test", "Test");
        var prepared = PreparedVisualSearch.ForUrl(new Uri("https://example.com"), null);
        var first = host.ShowAsync(descriptor, prepared, CancellationToken.None);
        using var secondCancellation = new CancellationTokenSource();
        var second = host.ShowAsync(descriptor, prepared, secondCancellation.Token);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, dispatcher.TryPostCalls);

        secondCancellation.Cancel();

        Assert.Equal(SearchBrowserShowStatus.Canceled, (await second).Status);
        Assert.Equal(1, dispatcher.TryPostCalls);
        host.Dispose();
        Assert.Equal(SearchBrowserShowStatus.Canceled, (await first).Status);
    }

    [Fact]
    public void Composition_root_preserves_shared_webview_thread_name()
        => Assert.Equal("CircleToSearch WebView2", CompositionRoot.SearchBrowserThreadName);

    [Fact]
    public async Task Stop_during_environment_wait_never_creates_view_and_reuses_stop_task()
    {
        var initialization = new TaskCompletionSource<CoreWebView2Environment>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new TestStaDispatcher { ExecutePostedAction = true };
        var viewCalls = 0;
        var host = CreateHost(dispatcher, () => initialization.Task,
            viewCreated: () => viewCalls++);
        var show = host.ShowAsync(
            new SearchProviderDescriptor("test", "Test"),
            PreparedVisualSearch.ForUrl(new Uri("https://example.com"), null),
            CancellationToken.None);

        var stop = host.StopAsync();
        Assert.Same(stop, host.StopAsync());
        Assert.False(stop.IsCompleted);
        Assert.Equal(0, dispatcher.StopCalls);
        initialization.SetException(new OperationCanceledException());

        Assert.Equal(SearchBrowserShowStatus.Canceled, (await show).Status);
        await stop;
        Assert.Equal(0, viewCalls);
        Assert.Equal(1, dispatcher.StopCalls);
    }

    private static SearchBrowserHost CreateHost(
        TestStaDispatcher dispatcher,
        Func<Task<CoreWebView2Environment>>? createEnvironment = null,
        TimeSpan? shutdownTimeout = null,
        Action? viewCreated = null)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CircleToSearch.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new SearchBrowserHost(
            AppContext.BaseDirectory,
            Path.Combine(directory, "Profile"),
            new PluginLog(directory),
            dispatcher,
            createEnvironment ?? (() => throw new InvalidOperationException("This test must not initialize a browser.")),
            (_, _, _) =>
            {
                viewCreated?.Invoke();
                throw new InvalidOperationException("This test must not create a view.");
            },
            shutdownTimeout);
    }
}
