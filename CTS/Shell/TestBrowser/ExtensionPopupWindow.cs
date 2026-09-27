using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CircleToSearch.Shell.TestBrowser;

// WebView2 has no toolbar, so the uBO Lite popup is hosted in a small window below its button.
internal static class ExtensionPopupWindow
{
    private const double InitialWidth = 340;
    private const double InitialHeight = 420;

    internal static async Task ShowAsync(
        FrameworkElement anchor,
        CoreWebView2Environment environment,
        string extensionId,
        string targetUrl,
        string pageToken,
        Action<string> openInBrowser)
    {
        var owner = Window.GetWindow(anchor)!;
        var webView = new WebView2();
        var window = new Window
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Width = InitialWidth,
            Height = InitialHeight,
            Content = webView,
        };
        var bottomRight = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
        var position = PresentationSource.FromVisual(anchor)!.CompositionTarget!.TransformFromDevice.Transform(bottomRight);
        window.Left = position.X - InitialWidth;
        window.Top = position.Y + 4;
        // Closing deactivates the window, and WPF throws when Close is called again while it closes.
        var closing = false;
        void Close()
        {
            if (!closing) window.Close();
        }
        window.Closing += (_, _) => closing = true;
        window.Closed += (_, _) => webView.Dispose();
        window.Deactivated += (_, _) => Close();
        window.Show();

        // The WPF control closes this window itself when the popup calls window.close(), as it does after starting the picker.
        await webView.EnsureCoreWebView2Async(environment);
        var core = webView.CoreWebView2;
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            openInBrowser(args.Uri);
            Close();
        };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PopupScript(targetUrl, pageToken));
        core.NavigationCompleted += async (_, _) =>
        {
            // The popup fills in after asking the extension about the page, so its size settles over a few frames.
            try
            {
                for (var attempt = 0; attempt < 10 && window.IsVisible; attempt++)
                {
                    var size = JsonSerializer.Deserialize<double[]>(await core.ExecuteScriptAsync(
                        "[document.body.offsetWidth, document.body.scrollHeight]"));
                    if (size is [> 0 and var width, > 0 and var height])
                    {
                        // Keeps the right edge under the button, like a browser toolbar popup.
                        window.Left = position.X - width;
                        window.Width = width;
                        window.Height = height;
                    }
                    await Task.Delay(100);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException
                                                  or System.Runtime.InteropServices.COMException) { }
        };
        core.Navigate($"chrome-extension://{extensionId}/popup.html");
    }

    // Each WebView2 is a tab of its own, so the popup would otherwise act on itself instead of the page,
    // and there is no browser to open the options page in. The token tells the page apart from a search
    // window showing the same address in this profile.
    private static string PopupScript(string targetUrl, string pageToken) => $$"""
        (() => {
            if (!location.pathname.endsWith('/popup.html')) return;
            const target = {{JsonSerializer.Serialize(targetUrl)}};
            const token = {{JsonSerializer.Serialize(pageToken)}};
            const query = chrome.tabs.query.bind(chrome.tabs);
            chrome.tabs.query = async info => {
                if (info?.active !== true || info?.currentWindow !== true) return query(info);
                const self = await chrome.tabs.getCurrent();
                const candidates = (await query({})).filter(tab => tab.id !== self?.id && tab.url === target);
                for (const tab of candidates) {
                    try {
                        const [frame] = await chrome.scripting.executeScript({
                            target: { tabId: tab.id },
                            world: 'MAIN',
                            func: () => window.__circleFlowTestBrowser,
                        });
                        if (frame?.result === token) return [tab];
                    } catch { }
                }
                return candidates.length > 0 ? [candidates[0]] : query(info);
            };
            chrome.runtime.openOptionsPage = () => window.open(chrome.runtime.getURL('/dashboard.html'));
        })();
        """;
}
