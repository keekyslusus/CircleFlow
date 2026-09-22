using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchBrowserHostLifecycleLiveTests
{
    [Theory]
    [Trait("Category", "Live")]
    [InlineData("document.querySelector('a').click()", true)]
    [InlineData("window.open('/target.html', '_blank')", true)]
    [InlineData("window.open('about:blank', '_blank')", false)]
    [InlineData("window.open('javascript:alert(1)', '_blank')", false)]
    public async Task Popup_requests_stay_in_the_configured_browser(string script, bool navigates)
    {
        if (!Enabled()) return;
        var directory = Path.Combine(TestOutputPaths.TempDirectory,
            "CircleFlow.PopupLinks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "index.html"),
            "<!doctype html><a href='/target.html' target='_blank' rel='noopener'>Open</a>");
        await File.WriteAllTextAsync(Path.Combine(directory, "target.html"),
            "<!doctype html><body style='margin:0'><div style='height:3000px'>Target</div></body>");
        var (host, dispatcher, views) = CreateHost();
        await using (host)
        {
            Assert.Equal(SearchBrowserShowStatus.Shown, (await host.ShowAsync(Descriptor(),
                PreparedVisualSearch.ForBrowserOperation(new ImmediateOperation(), null),
                CancellationToken.None)).Status);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(dispatcher.TryPost(async () =>
            {
                try
                {
                    var view = Assert.Single(views);
                    var grid = Assert.IsType<System.Windows.Controls.Grid>(view.Window.Content);
                    var core = grid.Children.OfType<WebView2>().Single().CoreWebView2;
                    core.SetVirtualHostNameToFolderMapping("popup.test", directory,
                        CoreWebView2HostResourceAccessKind.DenyCors);
                    async Task Navigate(Action start)
                    {
                        var finished = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
                            => finished.TrySetResult(args.IsSuccess);
                        core.NavigationCompleted += OnCompleted;
                        try
                        {
                            start();
                            Assert.True(await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                        }
                        finally { core.NavigationCompleted -= OnCompleted; }
                    }

                    await Navigate(() => core.Navigate("https://popup.test/index.html"));
                    var popupHandled = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnPopup(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
                        => popupHandled.TrySetResult(args.Handled && args.NewWindow is null);
                    core.NewWindowRequested += OnPopup;
                    try
                    {
                        if (navigates)
                            await Navigate(() => _ = core.ExecuteScriptAsync(script));
                        else
                            await core.ExecuteScriptAsync(script);
                        Assert.True(await popupHandled.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                        Assert.Equal(navigates ? "https://popup.test/target.html" : "https://popup.test/index.html",
                            core.Source);
                        Assert.False(view.IsClosed);
                        Assert.Single(views);
                        Assert.Equal("true", await core.ExecuteScriptAsync(
                            "document.querySelector('[data-circle-flow-scrollbar=overlay]') !== null"));
                        if (navigates)
                        {
                            Assert.Equal("true", await core.ExecuteScriptAsync(
                                "window.innerWidth === document.documentElement.clientWidth"));
                            Assert.Equal("true", await core.ExecuteScriptAsync(
                                "window.scrollTo(0, 500); window.scrollY > 0"));
                            Assert.True(core.CanGoBack);
                            await Navigate(core.GoBack);
                            Assert.Equal("https://popup.test/index.html", core.Source);
                        }
                    }
                    finally { core.NewWindowRequested -= OnPopup; }
                    completion.SetResult();
                }
                catch (Exception exception) { completion.SetException(exception); }
            }));
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
    }

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
