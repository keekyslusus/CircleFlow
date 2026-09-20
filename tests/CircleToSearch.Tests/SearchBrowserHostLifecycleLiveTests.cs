using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchBrowserHostLifecycleLiveTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Successful_shows_reuse_view_until_close()
    {
        if (!Enabled()) return;
        var (host, dispatcher, views) = CreateHost();
        await using (host)
        {
            var prepared = PreparedVisualSearch.ForBrowserOperation(
                new ImmediateOperation(), null);
            Assert.Equal(SearchBrowserShowStatus.Shown, (await host.ShowAsync(
                Descriptor(), prepared, CancellationToken.None)).Status);
            Assert.Equal(SearchBrowserShowStatus.Shown, (await host.ShowAsync(
                Descriptor(), prepared, CancellationToken.None)).Status);
            Assert.Single(views);

            dispatcher.Send(() => views[0].Window.Close());
            Assert.True(views[0].IsClosed);
            Assert.Equal(SearchBrowserShowStatus.Shown, (await host.ShowAsync(
                Descriptor(), prepared, CancellationToken.None)).Status);
            Assert.Equal(2, views.Count);
            await host.StopAsync();
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Close_cancels_operation_and_stop_waits_for_its_ui_path()
    {
        if (!Enabled()) return;
        var (host, dispatcher, views) = CreateHost();
        await using (host)
        {
            var operation = new HeldOperation();
            var show = host.ShowAsync(Descriptor(),
                PreparedVisualSearch.ForBrowserOperation(operation, null),
                CancellationToken.None);
            await operation.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            dispatcher.Send(() => views[0].Window.Close());
            var stop = host.StopAsync();
            Assert.False(stop.IsCompleted);
            operation.Release();
            Assert.Equal(SearchBrowserShowStatus.Canceled, (await show).Status);
            await stop;
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Factory_failure_allows_retry_and_stop()
    {
        if (!Enabled()) return;
        var attempts = 0;
        var (host, _, views) = CreateHost(() => ++attempts == 1);
        await using (host)
        {
            var prepared = PreparedVisualSearch.ForBrowserOperation(
                new ImmediateOperation(), null);
            Assert.Equal(SearchBrowserShowStatus.InitializationFailed, (await host.ShowAsync(
                Descriptor(), prepared, CancellationToken.None)).Status);
            Assert.Empty(views);
            Assert.Equal(SearchBrowserShowStatus.Shown, (await host.ShowAsync(
                Descriptor(), prepared, CancellationToken.None)).Status);
            Assert.Single(views);
            await host.StopAsync();
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Close_during_entrance_skips_browser_initialization_and_operation()
    {
        if (!Enabled()) return;
        var (host, _, views) = CreateHost(closeDuringEntrance: true);
        await using (host)
        {
            var operation = new CountingOperation();
            var result = await host.ShowAsync(Descriptor(),
                PreparedVisualSearch.ForBrowserOperation(operation, null),
                CancellationToken.None);
            Assert.Equal(SearchBrowserShowStatus.Canceled, result.Status);
            Assert.Single(views);
            Assert.True(views[0].IsClosed);
            Assert.Equal(0, operation.Calls);
            await host.StopAsync();
        }
    }

    private static bool Enabled() =>
        Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") == "1";

    private static SearchProviderDescriptor Descriptor() => new("test", "Test");

    private static (SearchBrowserHost Host, StaDispatcher Dispatcher, List<SearchBrowserWindowView> Views)
        CreateHost(Func<bool>? failFactory = null, bool closeDuringEntrance = false)
    {
        var paths = new AppPaths(Path.Combine(
            TestOutputPaths.TempDirectory, "CircleFlow.HostLifecycle", Guid.NewGuid().ToString("N")));
        AppDataDirectory.Initialize(paths);
        var dispatcher = new StaDispatcher(CompositionRoot.SearchBrowserThreadName);
        var views = new List<SearchBrowserWindowView>();
        var environments = new WebViewEnvironmentFactory(
            paths, TestUiStrings.English, new TestPluginNotifier());
        var host = new SearchBrowserHost(
            AppContext.BaseDirectory,
            paths.SearchProfileDirectory,
            new PluginLog(paths.LogsDirectory),
            dispatcher,
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true),
            (content, anchor, lightTheme) =>
            {
                if (failFactory?.Invoke() == true) throw new InvalidOperationException("Factory failed.");
                var view = closeDuringEntrance
                    ? new SearchBrowserWindowView(
                        TestUiStrings.English, content, lightTheme, false,
                        window => new BottomResultsPanel(window, anchor, animationsEnabled: true))
                    : CompositionRoot.CreateSearchBrowserWindowView(
                        TestUiStrings.English, content, anchor, lightTheme);
                if (closeDuringEntrance)
                    view.Window.Loaded += (_, _) =>
                        view.Window.Dispatcher.BeginInvoke(() => view.Window.Close());
                views.Add(view);
                return view;
            });
        return (host, dispatcher, views);
    }

    private sealed class ImmediateOperation : IVisualSearchBrowserOperation
    {
        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session, CancellationToken cancel) =>
            Task.FromResult(VisualSearchBrowserOperationStatus.Succeeded);
    }

    private sealed class HeldOperation : IVisualSearchBrowserOperation
    {
        private readonly TaskCompletionSource<VisualSearchBrowserOperationStatus> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;

        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session, CancellationToken cancel)
        {
            _entered.TrySetResult();
            return _completion.Task;
        }

        internal void Release() => _completion.TrySetResult(VisualSearchBrowserOperationStatus.Succeeded);
    }

    private sealed class CountingOperation : IVisualSearchBrowserOperation
    {
        internal int Calls { get; private set; }

        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session, CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(VisualSearchBrowserOperationStatus.Succeeded);
        }
    }
}
