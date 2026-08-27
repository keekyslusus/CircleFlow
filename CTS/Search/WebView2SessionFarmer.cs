using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CircleToSearch.Search;

// Mints the Lens session with a genuine Chromium network stack: a hidden off-screen WebView2
// opens google.com once, the cookie manager returns the browser-graded NID/AEC, the window shuts
// down. Runs on its own STA thread; no WebView2 exists between farms.
public static class WebView2SessionFarmer
{
    public static async Task<LensSession?> FarmAsync(string pluginDirectory, string userDataFolder, PluginLog log, TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<LensSession?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // Flow loads plugins from a non-app directory; without an explicit preload the
                // native WebView2Loader.dll is not on the probing path.
                NativeLibrary.TryLoad(Path.Combine(pluginDirectory, "WebView2Loader.dll"), out _);

                var dispatcher = Dispatcher.CurrentDispatcher;
                var webView = new WebView2();
                var window = new Window
                {
                    Content = webView,
                    Left = -32000,
                    Top = 0,
                    Width = 800,
                    Height = 600,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                };
                window.Show();
                // EnsureCoreWebView2Async refuses to run before the dispatcher pump is live,
                // so the farm is queued and starts only once Dispatcher.Run() is executing.
                dispatcher.BeginInvoke(new Action(() => RunFarm(webView, userDataFolder, log, completion, dispatcher)), DispatcherPriority.Normal);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                log.Error(nameof(WebView2SessionFarmer), "session farming thread failed", exception);
                completion.TrySetResult(null);
            }
        })
        {
            IsBackground = true,
            Name = "CircleToSearch webview2 session",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var finished = await Task.WhenAny(completion.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != completion.Task)
        {
            log.Warn(nameof(WebView2SessionFarmer), $"session farming timed out after {timeout.TotalSeconds:F0}s");
            return null;
        }
        return completion.Task.Result;
    }

    private static async void RunFarm(WebView2 webView, string userDataFolder, PluginLog log, TaskCompletionSource<LensSession?> completion, Dispatcher dispatcher)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
            await webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.NavigationCompleted += (_, args) =>
            {
                if (webView.CoreWebView2?.Source?.StartsWith("https://www.google.com", StringComparison.Ordinal) == true)
                    navigated.TrySetResult(args.IsSuccess);
            };
            webView.CoreWebView2.Navigate("https://www.google.com/");
            var succeeded = await navigated.Task.ConfigureAwait(true);
            if (!succeeded) throw new InvalidOperationException("navigation to google.com failed");
            await Task.Delay(700).ConfigureAwait(true);

            var farmed = (await webView.CoreWebView2.CookieManager
                .GetCookiesAsync("https://www.google.com/").ConfigureAwait(true))
                .Where(c => !string.IsNullOrEmpty(c.Value))
                .Select(c => new LensCookie(c.Name, c.Value, c.Domain, string.IsNullOrEmpty(c.Path) ? "/" : c.Path))
                .ToList();
            log.Info(nameof(WebView2SessionFarmer), $"farmed {farmed.Count} session cookies: {string.Join(", ", farmed.Select(c => c.Name))}");
            completion.TrySetResult(farmed.Count == 0 ? null : new LensSession { IssuedAt = DateTimeOffset.UtcNow, Cookies = farmed });
        }
        catch (Exception exception)
        {
            log.Error(nameof(WebView2SessionFarmer), "session farming failed", exception);
            completion.TrySetResult(null);
        }
        finally
        {
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
    }
}
