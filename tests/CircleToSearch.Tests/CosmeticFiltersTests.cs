using CircleToSearch.Interop;
using CircleToSearch.Search.Browser;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class CosmeticFiltersTests
{
    [Fact]
    public void Parse_keeps_host_scoped_generic_and_excluded_rules_and_skips_what_css_cannot_apply()
    {
        var filters = CosmeticFilters.Parse(
        [
            "[Adblock Plus 2.0]",
            "! a comment",
            "",
            "||ads.example.com^",
            "example.com#@#.allowed",
            "example.com##+js(set-constant, x, 1)",
            "example.com##^script",
            "example.com##",
            "  Example.com, google.*  ##  .promo > a  ",
            "~Sub.example.com##.everywhere",
            "##.generic",
        ]);

        Assert.Collection(filters,
            filter =>
            {
                Assert.Equal(["example.com", "google.*"], filter.Hosts);
                Assert.Empty(filter.ExcludedHosts);
                Assert.Equal(".promo > a", filter.Selector);
            },
            filter =>
            {
                Assert.Empty(filter.Hosts);
                Assert.Equal(["sub.example.com"], filter.ExcludedHosts);
                Assert.Equal(".everywhere", filter.Selector);
            },
            filter =>
            {
                Assert.Empty(filter.Hosts);
                Assert.Empty(filter.ExcludedHosts);
                Assert.Equal(".generic", filter.Selector);
            });
    }

    [Fact]
    public void Bundled_filter_file_ships_with_the_app()
    {
        var path = CosmeticFilters.FilePath(AppContext.BaseDirectory);
        Assert.True(File.Exists(path));
        Assert.Equal(CosmeticFilters.CreateScript(CosmeticFilters.Parse(File.ReadAllLines(path))),
            CosmeticFilters.LoadScript(AppContext.BaseDirectory, new PluginLog(TestOutputPaths.TempDirectory)));
    }

    [Fact]
    public void Missing_filter_file_is_logged_and_applies_no_rules()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "CircleFlowFilterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Equal(CosmeticFilters.CreateScript([]), CosmeticFilters.LoadScript(directory, new PluginLog(directory)));
            Assert.Contains("operation=read-filters", File.ReadAllText(Path.Combine(directory, "plugin.log")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Script_hides_matching_elements_by_host_in_a_real_browser()
    {
        TestSwitches.Require("CTS_WEBVIEW2_LIVE");
        var script = CosmeticFilters.CreateScript(CosmeticFilters.Parse(
        [
            "example.com##.promo",
            "google.*##.banner",
            "~sub.example.com##.everywhere",
            "##.generic",
            "example.com##.broken:has-text(Try)",
            "example.com##.after-broken",
        ]));
        using var dispatcher = new StaDispatcher("Cosmetic filter test");
        var completion = new TaskCompletionSource<Dictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(dispatcher.TryPost(async () =>
        {
            System.Windows.Window? host = null;
            try
            {
                var profile = Path.Combine(TestOutputPaths.TempDirectory, "CircleFlowFilterLive", Guid.NewGuid().ToString("N"));
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                using var view = new WebView2();
                host = new System.Windows.Window { Content = view, Width = 640, Height = 480, ShowActivated = false };
                host.Show();
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document);
                core.WebResourceRequested += (_, args) => args.Response = environment.CreateWebResourceResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes(
                        "<!doctype html>" + string.Concat(
                            new[] { "promo", "banner", "everywhere", "generic", "broken", "after-broken" }
                                .Select(name => $"<div class=\"{name}\">x</div>")) +
                        // Pages that manage their own constructed sheets replace the whole list.
                        "<script>document.adoptedStyleSheets = [new CSSStyleSheet()];</script>")),
                    200, "OK", "Content-Type: text/html");
                var results = new Dictionary<string, string>();
                foreach (var page in new[] { "www.example.com", "sub.example.com", "www.google.co.uk", "notgoogle.com" })
                {
                    var loaded = new TaskCompletionSource();
                    void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult();
                    core.NavigationCompleted += Completed;
                    core.Navigate($"https://{page}/");
                    await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    core.NavigationCompleted -= Completed;
                    results[page] = System.Text.Json.JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(
                        "[...document.querySelectorAll('div')].filter(d => getComputedStyle(d).display === 'none').map(d => d.className).join(' ')"))!;
                }
                completion.SetResult(results);
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { host?.Close(); }
        }));
        var hidden = await completion.Task.WaitAsync(TimeSpan.FromMinutes(1));

        Assert.Equal("promo everywhere generic after-broken", hidden["www.example.com"]);
        Assert.Equal("promo generic after-broken", hidden["sub.example.com"]);
        Assert.Equal("banner everywhere generic", hidden["www.google.co.uk"]);
        Assert.Equal("everywhere generic", hidden["notgoogle.com"]);
    }
}
