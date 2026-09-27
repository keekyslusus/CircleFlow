using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CircleToSearch.Interop;
using CircleToSearch.Shell.TestBrowser;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TestBrowserWindowLiveTests
{
    private const string PageUrl = "https://circleflow-test.invalid/";

    [Fact]
    [Trait("Category", "Live")]
    public async Task Extension_popup_acts_on_its_own_page_opens_the_dashboard_and_starts_the_picker()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;
        var profile = Path.Combine(Path.GetTempPath(), "CircleFlowTestBrowserLive", Guid.NewGuid().ToString("N"));
        using var dispatcher = new StaDispatcher("Test browser live test");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(dispatcher.TryPost(async () =>
        {
            TestBrowserWindow? browser = null;
            Window? decoyWindow = null;
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile,
                    options: new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
                // An older window on the same address, like a search window, comes first in the tab list and must not get the picker.
                var decoy = new WebView2();
                decoyWindow = new Window { Content = decoy, Width = 400, Height = 300, ShowActivated = false };
                decoyWindow.Show();
                await decoy.EnsureCoreWebView2Async(environment);
                ServePage(decoy.CoreWebView2, environment);
                await Navigate(decoy.CoreWebView2, PageUrl);

                browser = new TestBrowserWindow(TestUiStrings.English, lightTheme: true, () => Task.FromResult(environment),
                    AppContext.BaseDirectory, profile, () => { }, new PluginLog(profile));
                browser.Window.Show();
                var content = (DockPanel)browser.Window.Content;
                var page = content.Children.OfType<WebView2>().Single();
                var popupButton = content.Children.OfType<DockPanel>().Single().Children.OfType<Button>()
                    .Single(button => (string)button.Content == TestUiStrings.English.TestBrowserText("ubol"));
                await WaitFor(() => popupButton.IsEnabled);
                ServePage(page.CoreWebView2, environment);

                await Navigate(page.CoreWebView2, PageUrl);
                var popup = await OpenPopup(browser.Window, popupButton);
                Assert.Equal("\"circleflow-test.invalid\"",
                    await popup.CoreWebView2.ExecuteScriptAsync("document.querySelector('#hostname').textContent"));
                // The popup's own gear handler ignores synthetic clicks, so this calls what it calls.
                await popup.CoreWebView2.ExecuteScriptAsync("chrome.runtime.openOptionsPage()");
                await WaitFor(() => browser.Window.OwnedWindows.Count == 0 &&
                    page.CoreWebView2.Source.EndsWith("/dashboard.html", StringComparison.Ordinal));

                await Navigate(page.CoreWebView2, PageUrl);
                popup = await OpenPopup(browser.Window, popupButton);
                await popup.CoreWebView2.ExecuteScriptAsync("document.querySelector('#gotoPicker').click()");
                await WaitFor(() => browser.Window.OwnedWindows.Count == 0);
                for (var attempt = 0; attempt < 100 &&
                     await page.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('iframe').length") == "0"; attempt++)
                    await Task.Delay(50);
                Assert.Equal("1", await page.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('iframe').length"));
                Assert.Equal("0", await decoy.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('iframe').length"));
                completion.SetResult();
            }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally
            {
                decoyWindow?.Close();
                browser?.Window.Close();
            }
        }));
        await completion.Task.WaitAsync(TimeSpan.FromMinutes(1));
    }

    private static async Task<WebView2> OpenPopup(Window owner, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WebView2? popup = null;
        await WaitFor(() => (popup = owner.OwnedWindows.OfType<Window>().FirstOrDefault()?.Content as WebView2)?.CoreWebView2 is not null);
        for (var attempt = 0; attempt < 100 &&
             await popup!.CoreWebView2.ExecuteScriptAsync("document.querySelector('#hostname')?.textContent.includes('.') === true") != "true"; attempt++)
            await Task.Delay(50);
        return popup!;
    }

    private static void ServePage(CoreWebView2 core, CoreWebView2Environment environment)
    {
        core.AddWebResourceRequestedFilter(PageUrl + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) => args.Response = environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes("<!doctype html><h1>CircleFlow</h1>")), 200, "OK",
            "Content-Type: text/html");
    }

    private static async Task Navigate(CoreWebView2 core, string uri)
    {
        // The dashboard opened by the popup may still be loading, so only this navigation's completion counts.
        ulong? navigationId = null;
        var completion = new TaskCompletionSource<bool>();
        void Starting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (args.Uri == uri) navigationId = args.NavigationId;
        }
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.NavigationId == navigationId) completion.TrySetResult(args.IsSuccess);
        }
        core.NavigationStarting += Starting;
        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(uri);
            Assert.True(await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally
        {
            core.NavigationStarting -= Starting;
            core.NavigationCompleted -= Completed;
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(50);
        Assert.True(condition());
    }
}
