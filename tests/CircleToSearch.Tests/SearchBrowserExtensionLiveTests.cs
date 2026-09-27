using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchBrowserExtensionLiveTests
{
    // uBO Lite redirects doubleclick requests to stubs, so the probe needs a host its rules block outright.
    private const string BlockedAdUrl = "https://adservice.google.com/adsid/google/ui";

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
                    SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile));
                Assert.True(extension.IsEnabled);
                await Navigate(core, $"chrome-extension://{extension.Id}/dashboard.html");
                Assert.Contains("uBO Lite", await core.ExecuteScriptAsync("document.title"));
                await core.ExecuteScriptAsync("chrome.declarativeNetRequest.getEnabledRulesets().then(x => window.rules = x)");
                await Task.Delay(1000);
                Assert.Contains("easylist", await core.ExecuteScriptAsync("JSON.stringify(window.rules)"));

                ServeTestPages(core, environment);
                await Navigate(core, "https://circleflow-test.invalid/");
                Assert.Equal("loaded", await Fetch(core, "https://circleflow-test.invalid/normal.js"));
                Assert.Equal("blocked", await Fetch(core, BlockedAdUrl));
                await extension.EnableAsync(false);
                Assert.Equal("loaded", await Fetch(core, BlockedAdUrl));
                await extension.EnableAsync(true);
                Assert.Equal("blocked", await Fetch(core, BlockedAdUrl));
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { host?.Close(); }
        }));
        await completion.Task.WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Remembered_extension_blocks_ads_in_a_new_browser_process_without_being_added_again()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;
        var profile = Path.Combine(Path.GetTempPath(), "CircleFlowExtensionLive", Guid.NewGuid().ToString("N"));
        var directory = SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile);
        var id = await RunInBrowser(profile, async (view, _) =>
        {
            var extension = await view.CoreWebView2.Profile.AddBrowserExtensionAsync(directory);
            SearchBrowserExtension.RememberInstalled(directory, extension.Id);
            return extension.Id;
        });

        Assert.Equal(directory, SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile));
        Assert.Equal(id, SearchBrowserExtension.InstalledId(directory));
        var outcome = await RunInBrowser(profile, async (view, environment) =>
        {
            var core = view.CoreWebView2;
            var extensions = await core.Profile.GetBrowserExtensionsAsync();
            Assert.Contains(extensions, extension => extension.Id == id && extension.IsEnabled);
            ServeTestPages(core, environment);
            return await Fetch(core, BlockedAdUrl);
        });
        Assert.Equal("blocked", outcome);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task First_window_adds_the_annoyance_lists_to_the_lists_uBO_Lite_chose()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;
        var profile = Path.Combine(Path.GetTempPath(), "CircleFlowExtensionLive", Guid.NewGuid().ToString("N"));
        var directory = SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile);

        var (enabled, defaults) = await RunInBrowser(profile, (view, _) => EnableAndReadListsAsync(view, profile, directory));

        Assert.Equal(string.Join(',', SearchBrowserExtension.AnnoyanceRulesets), SearchBrowserExtension.EnabledRulesets(directory));
        Assert.Contains("easylist", defaults);
        Assert.Superset(new HashSet<string>([.. defaults, .. SearchBrowserExtension.AnnoyanceRulesets]), enabled.ToHashSet());
        Assert.DoesNotContain("annoyances-cookies", enabled);
        Assert.DoesNotContain("annoyances-social", enabled);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Annoyance_lists_are_enabled_again_after_the_browser_data_is_cleared()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;
        var profile = Path.Combine(Path.GetTempPath(), "CircleFlowExtensionLive", Guid.NewGuid().ToString("N"));
        var directory = SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile);
        await RunInBrowser(profile, (view, _) => EnableAndReadListsAsync(view, profile, directory));

        // BrowserDataCleanup removes this folder, and uBO Lite's settings with it, but keeps the unpacked extension.
        Directory.Delete(Path.Combine(profile, "EBWebView"), recursive: true);
        var (enabled, _) = await RunInBrowser(profile, (view, _) => EnableAndReadListsAsync(view, profile, directory));

        Assert.Superset(new HashSet<string>(SearchBrowserExtension.AnnoyanceRulesets), enabled.ToHashSet());
    }

    // uBO Lite's own defaults include the regional list it picked for this system's languages.
    private static async Task<Rulesets> EnableAndReadListsAsync(WebView2 view, string profile, string directory)
    {
        var extension = await SearchBrowserExtension.EnsureEnabledAsync(
            view, AppContext.BaseDirectory, profile, new PluginLog(profile), CancellationToken.None);
        for (var attempt = 0; attempt < 600 && SearchBrowserExtension.EnabledRulesets(directory) is null; attempt++)
            await Task.Delay(50);

        await Navigate(view.CoreWebView2, $"chrome-extension://{extension.Id}/dashboard.html");
        await view.CoreWebView2.ExecuteScriptAsync("""
            window.rules = null;
            Promise.all([
                chrome.declarativeNetRequest.getEnabledRulesets(),
                chrome.runtime.sendMessage({ what: 'getDefaultConfig' }),
            ]).then(([enabled, defaults]) => window.rules = { enabled, defaults: defaults.rulesets });
            """);
        string rules;
        for (var attempt = 0; (rules = await view.CoreWebView2.ExecuteScriptAsync("window.rules")) == "null" && attempt < 100; attempt++)
            await Task.Delay(50);
        return System.Text.Json.JsonSerializer.Deserialize<Rulesets>(rules)!;
    }

    private sealed record Rulesets(
        [property: System.Text.Json.Serialization.JsonPropertyName("enabled")] string[] Enabled,
        [property: System.Text.Json.Serialization.JsonPropertyName("defaults")] string[] Defaults);

    private static async Task<T> RunInBrowser<T>(
        string profile,
        Func<WebView2, CoreWebView2Environment, Task<T>> body)
    {
        using var dispatcher = new StaDispatcher("Extension integration test");
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(dispatcher.TryPost(async () =>
        {
            System.Windows.Window? host = null;
            WebView2? view = null;
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile,
                    options: new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
                environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
                view = new WebView2();
                host = new System.Windows.Window { Content = view, Width = 640, Height = 480, ShowActivated = false };
                host.Show();
                await view.EnsureCoreWebView2Async(environment);
                completion.SetResult(await body(view, environment));
            }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally
            {
                host?.Close();
                view?.Dispose();
            }
        }));
        var result = await completion.Task.WaitAsync(TimeSpan.FromMinutes(1));
        // The next run must start a fresh browser process that loads the extension from the profile.
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return result;
    }

    private static void ServeTestPages(CoreWebView2 core, CoreWebView2Environment environment)
    {
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
