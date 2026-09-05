using CircleToSearch.Interop;
using CircleToSearch.Search;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class LensBrowserExtensionLiveTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Extension_blocks_ad_requests_but_allows_normal_resources_and_can_be_disabled()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;
        using var dispatcher = new StaDispatcher("Extension integration test");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(dispatcher.TryPost(async () =>
        {
            System.Windows.Window? host = null;
            try
            {
                var profile = Path.Combine(Path.GetTempPath(), "CircleFlowExtensionLive", Guid.NewGuid().ToString("N"));
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile,
                    options: new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
                using var view = new WebView2();
                host = new System.Windows.Window { Content = view, Width = 640, Height = 480, ShowActivated = false };
                host.Show();
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                var extension = await core.Profile.AddBrowserExtensionAsync(
                    LensBrowserExtension.Prepare(AppContext.BaseDirectory, profile));
                Assert.True(extension.IsEnabled);
                await Navigate(core, $"chrome-extension://{extension.Id}/dashboard.html");
                Assert.Contains("uBO Lite", await core.ExecuteScriptAsync("document.title"));
                await core.ExecuteScriptAsync("chrome.declarativeNetRequest.getEnabledRulesets().then(x => window.rules = x)");
                await Task.Delay(1000);
                Assert.Contains("easylist", await core.ExecuteScriptAsync("JSON.stringify(window.rules)"));

                core.AddWebResourceRequestedFilter("https://circleflow-test.invalid/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, args) =>
                {
                    var uri = new Uri(args.Request.Uri);
                    var target = Uri.UnescapeDataString(uri.Query.TrimStart('?'));
                    var html = "<!doctype html><script>window.result=null; fetch(" +
                        System.Text.Json.JsonSerializer.Serialize(target.Length > 0 ? target : "/normal.js") +
                        ", {cache:'no-store',mode:'no-cors'}).then(()=>window.result='loaded',()=>window.result='blocked');</script>";
                    args.Response = environment.CreateWebResourceResponse(
                        new MemoryStream(Encoding.UTF8.GetBytes(uri.AbsolutePath.EndsWith(".js") ? "/* test */" : html)),
                        200, "OK", "Content-Type: " + (uri.AbsolutePath.EndsWith(".js") ? "application/javascript" : "text/html") + "\r\nCache-Control: no-store");
                };
                await Navigate(core, "https://circleflow-test.invalid/");
                Assert.Equal("loaded", await Fetch(core, "https://circleflow-test.invalid/normal.js"));
                Assert.Equal("blocked", await Fetch(core, "https://googleads.g.doubleclick.net/pagead/ads"));
                await extension.EnableAsync(false);
                Assert.Equal("loaded", await Fetch(core, "https://googleads.g.doubleclick.net/pagead/ads"));
                await extension.EnableAsync(true);
                Assert.Equal("blocked", await Fetch(core, "https://googleads.g.doubleclick.net/pagead/ads"));
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { host?.Close(); }
        }));
        await completion.Task.WaitAsync(TimeSpan.FromMinutes(1));
    }

    private static async Task Navigate(CoreWebView2 core, string uri)
    {
        var completion = new TaskCompletionSource<bool>();
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) => completion.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(uri);
            Assert.True(await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally { core.NavigationCompleted -= Completed; }
    }

    private static async Task<string> Fetch(CoreWebView2 core, string uri)
    {
        await Navigate(core, "https://circleflow-test.invalid/?" + Uri.EscapeDataString(uri));
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var result = await core.ExecuteScriptAsync("window.result");
            if (result != "null") return System.Text.Json.JsonSerializer.Deserialize<string>(result)!;
            await Task.Delay(50);
        }
        throw new TimeoutException("Resource fetch did not finish.");
    }
}
