using CircleToSearch.Interop;
using CircleToSearch.Search.Browser;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class OverlayScrollbarLiveTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Native_scrollbar_is_hidden_during_parsing_on_every_navigation()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;
        using var dispatcher = new StaDispatcher("Early scrollbar integration test");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(dispatcher.TryPost(async () =>
        {
            System.Windows.Window? window = null;
            try
            {
                var profile = Path.Combine(TestOutputPaths.TempDirectory, "CircleFlowOverlayScrollbarLive", Guid.NewGuid().ToString("N"));
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                using var view = new WebView2();
                window = new System.Windows.Window { Content = view, Width = 640, Height = 480, ShowActivated = false };
                window.Show();
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(OverlayScrollbarScript.Create());
                for (var navigation = 0; navigation < 2; navigation++)
                {
                    await NavigateToString(core, """
                        <!doctype html><html><body style="margin:0"><div style="height:2000px"></div>
                        <script>
                        window.earlyScrollbarHidden = document.readyState === 'loading'
                            && getComputedStyle(document.documentElement).scrollbarWidth === 'none'
                            && window.innerWidth === document.documentElement.clientWidth
                            && !document.querySelector('[data-circle-flow-scrollbar=overlay]');
                        </script></body></html>
                        """);
                    Assert.Equal("true", await core.ExecuteScriptAsync("window.earlyScrollbarHidden"));
                    Assert.Equal("true", await core.ExecuteScriptAsync(
                        "document.querySelector('[data-circle-flow-scrollbar=overlay]') !== null"));
                }
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { window?.Close(); }
        }));
        await completion.Task.WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Scrollbar_is_overlayed_and_hides_after_idle_delay()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;
        using var dispatcher = new StaDispatcher("Overlay scrollbar integration test");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(dispatcher.TryPost(async () =>
        {
            System.Windows.Window? hostWindow = null;
            try
            {
                var profile = Path.Combine(
                    TestOutputPaths.TempDirectory,
                    "CircleFlowOverlayScrollbarLive",
                    Guid.NewGuid().ToString("N"));
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                using var view = new WebView2();
                hostWindow = new System.Windows.Window
                {
                    Content = view,
                    Width = 640,
                    Height = 480,
                    ShowActivated = false,
                };
                hostWindow.Show();
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(OverlayScrollbarScript.Create());
                await NavigateToString(core, "<!doctype html><html><body style='margin:0'><div style='height:2000px'></div></body></html>");

                Assert.Equal("true", await core.ExecuteScriptAsync(
                    "document.querySelector('[data-circle-flow-scrollbar=overlay]') !== null"));
                Assert.Equal("true", await core.ExecuteScriptAsync(
                    "window.innerWidth === document.documentElement.clientWidth"));
                Assert.Equal("\"4px\"", await core.ExecuteScriptAsync(
                    "getComputedStyle(document.querySelector('[data-circle-flow-scrollbar=overlay]').shadowRoot.querySelector('[part=thumb]')).width"));

                await core.ExecuteScriptAsync("window.scrollTo(0, 500)");
                await WaitForScriptResult(core, """
                    (() => {
                        const host = document.querySelector('[data-circle-flow-scrollbar=overlay]');
                        return host.getAnimations().some(animation =>
                            animation instanceof CSSTransition && animation.transitionProperty === 'opacity');
                    })()
                    """, "true");
                await WaitForScriptResult(core,
                    "getComputedStyle(document.querySelector('[data-circle-flow-scrollbar=overlay]')).opacity",
                    "\"1\"");

                await WaitForScriptResult(core, """
                    (() => {
                        const host = document.querySelector('[data-circle-flow-scrollbar=overlay]');
                        return host.style.opacity === '0' && host.getAnimations().some(animation =>
                            animation instanceof CSSTransition && animation.transitionProperty === 'opacity');
                    })()
                    """, "true");

                await WaitForScriptResult(core,
                    "getComputedStyle(document.querySelector('[data-circle-flow-scrollbar=overlay]')).opacity",
                    "\"0\"");

                Assert.Equal("\"0\"", await core.ExecuteScriptAsync(
                    "getComputedStyle(document.querySelector('[data-circle-flow-scrollbar=overlay]')).opacity"));

                await NavigateToString(core, "<!doctype html><html><body>Short page</body></html>");

                Assert.Equal("\"none\"", await core.ExecuteScriptAsync(
                    "getComputedStyle(document.querySelector('[data-circle-flow-scrollbar=overlay]')).display"));
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                hostWindow?.Close();
            }
        }));
        await completion.Task.WaitAsync(TimeSpan.FromMinutes(1));
    }

    private static async Task NavigateToString(CoreWebView2 core, string html)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
            completion.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += Completed;
        try
        {
            core.NavigateToString(html);
            Assert.True(await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally
        {
            core.NavigationCompleted -= Completed;
        }
    }

    private static async Task WaitForScriptResult(CoreWebView2 core, string script, string expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await core.ExecuteScriptAsync(script) == expected) return;
            await Task.Delay(20);
        }

        Assert.Fail($"Script did not return {expected}: {script}");
    }
}
