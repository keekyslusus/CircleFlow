using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using CircleToSearch.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CircleToSearch.Search;

public sealed class WebView2SearchWindow : IDisposable
{
    private static readonly Uri GoogleLensHome = new("https://lens.google.com/?hl=ru");
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AttachmentTimeout = TimeSpan.FromSeconds(15);

    private readonly string _pluginDirectory;
    private readonly string _userDataFolder;
    private readonly PluginLog _log;
    private readonly StaDispatcher _dispatcher = new("CircleToSearch WebView2");
    private CoreWebView2Environment? _environment;
    private Window? _window;
    private WebView2? _webView;
    private bool _disposed;

    public WebView2SearchWindow(string pluginDirectory, string userDataFolder, PluginLog log)
    {
        _pluginDirectory = pluginDirectory;
        _userDataFolder = userDataFolder;
        _log = log;
    }

    public static string? GetRuntimeVersion(string pluginDirectory)
    {
        try
        {
            NativeLibrary.TryLoad(Path.Combine(pluginDirectory, "WebView2Loader.dll"), out _);
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            return null;
        }
    }

    public async Task<WebView2SearchStatus> ShowAsync(byte[] png, CancellationToken cancel)
    {
        if (_disposed) return WebView2SearchStatus.Failed;
        if (cancel.IsCancellationRequested) return WebView2SearchStatus.Canceled;

        var completion = new TaskCompletionSource<WebView2SearchStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryPost(() => _ = ShowOnUiThreadAsync(png, cancel, completion)))
            return WebView2SearchStatus.Failed;

        using var registration = cancel.Register(
            () => completion.TrySetResult(WebView2SearchStatus.Canceled));
        return await completion.Task.ConfigureAwait(false);
    }

    private async Task ShowOnUiThreadAsync(
        byte[] png,
        CancellationToken cancel,
        TaskCompletionSource<WebView2SearchStatus> completion)
    {
        try
        {
            cancel.ThrowIfCancellationRequested();
            await EnsureWindowAsync().ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();

            var window = _window!;
            var webView = _webView!;
            window.Show();
            window.Activate();

            await NavigateAsync(webView, GoogleLensHome, cancel).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();

            var resultsReady = await OpenLensResultsAsync(webView, window, png, cancel)
                .ConfigureAwait(true);
            completion.TrySetResult(resultsReady
                ? WebView2SearchStatus.ResultsReady
                : WebView2SearchStatus.Failed);
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            _log.Error(nameof(WebView2SearchWindow), "WebView2 Runtime is unavailable", exception);
            completion.TrySetResult(WebView2SearchStatus.RuntimeUnavailable);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetResult(WebView2SearchStatus.Canceled);
        }
        catch (Exception exception)
        {
            _log.Error(nameof(WebView2SearchWindow), "opening Google Lens failed", exception);
            completion.TrySetResult(WebView2SearchStatus.Failed);
        }
    }

    private async Task EnsureWindowAsync()
    {
        if (_window is not null && _webView is not null) return;

        NativeLibrary.TryLoad(Path.Combine(_pluginDirectory, "WebView2Loader.dll"), out _);
        Directory.CreateDirectory(_userDataFolder);
        _environment ??= await CoreWebView2Environment
            .CreateAsync(userDataFolder: _userDataFolder)
            .ConfigureAwait(true);

        var webView = new WebView2();
        var window = new Window
        {
            Title = "Circle to Search — Google Lens",
            Width = 1200,
            Height = 820,
            MinWidth = 720,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = webView,
        };
        window.Closed += (_, _) =>
        {
            webView.Dispose();
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                _webView = null;
            }
        };

        try
        {
            window.Show();
            await webView.EnsureCoreWebView2Async(_environment).ConfigureAwait(true);
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
        }
        catch
        {
            window.Close();
            webView.Dispose();
            throw;
        }

        _window = window;
        _webView = webView;
    }

    private static async Task NavigateAsync(WebView2 webView, Uri target, CancellationToken cancel)
    {
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            => completion.TrySetResult(args);

        webView.NavigationCompleted += OnCompleted;
        try
        {
            webView.CoreWebView2.Navigate(target.AbsoluteUri);
            var result = await completion.Task
                .WaitAsync(NavigationTimeout, cancel)
                .ConfigureAwait(true);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"Google navigation failed: {result.WebErrorStatus}");
        }
        finally
        {
            webView.NavigationCompleted -= OnCompleted;
        }
    }

    private async Task<bool> OpenLensResultsAsync(
        WebView2 webView,
        Window window,
        byte[] png,
        CancellationToken cancel)
    {
        var acknowledgement = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var navigation = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            var message = args.TryGetWebMessageAsString();
            if (message.StartsWith("CTS:", StringComparison.Ordinal))
                acknowledgement.TrySetResult(message);
        }

        void OnClosed(object? sender, EventArgs args) => closed.TrySetResult();
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            => navigation.TrySetResult(args);

        webView.CoreWebView2.WebMessageReceived += OnMessage;
        webView.NavigationCompleted += OnNavigationCompleted;
        window.Closed += OnClosed;
        try
        {
            var installed = await webView.CoreWebView2
                .ExecuteScriptAsync(AttachmentBridgeScript)
                .ConfigureAwait(true);
            if (!string.Equals(JsonSerializer.Deserialize<string>(installed), "ready", StringComparison.Ordinal))
                return false;

            webView.CoreWebView2.PostWebMessageAsString(Convert.ToBase64String(png));
            var finished = await Task.WhenAny(
                    acknowledgement.Task,
                    closed.Task,
                    Task.Delay(AttachmentTimeout, cancel))
                .ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (finished != acknowledgement.Task)
            {
                _log.Warn(nameof(WebView2SearchWindow), "Google Lens upload acknowledgement timed out");
                return false;
            }

            var message = await acknowledgement.Task.ConfigureAwait(true);
            _log.Info(nameof(WebView2SearchWindow), $"Google Lens upload response: {message}");
            if (!string.Equals(message, "CTS:submitted", StringComparison.Ordinal)) return false;

            var navigated = await Task.WhenAny(
                    navigation.Task,
                    closed.Task,
                    Task.Delay(NavigationTimeout, cancel))
                .ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (navigated != navigation.Task)
            {
                _log.Warn(nameof(WebView2SearchWindow), "Google Lens results navigation timed out");
                return false;
            }

            var result = await navigation.Task.ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                _log.Warn(
                    nameof(WebView2SearchWindow),
                    $"Google Lens results navigation failed: {result.WebErrorStatus}");
                return false;
            }

            _log.Info(nameof(WebView2SearchWindow), "Google Lens results opened");
            return IsGoogleLensResultsUrl(webView.Source);
        }
        finally
        {
            webView.CoreWebView2.WebMessageReceived -= OnMessage;
            webView.NavigationCompleted -= OnNavigationCompleted;
            window.Closed -= OnClosed;
        }
    }

    private static bool IsGoogleLensResultsUrl(Uri? uri)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.Host == "lens.google.com")
            return uri.AbsolutePath.StartsWith("/search", StringComparison.Ordinal);

        if (uri.Host is not ("google.com" or "www.google.com") || uri.AbsolutePath != "/search")
            return false;

        return uri.Query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part is "?udm=26" or "udm=26");
    }

    private const string AttachmentBridgeScript = """
        (() => {
          if (window.__circleToSearchBridgeInstalled) return "ready";
          window.__circleToSearchBridgeInstalled = true;
          window.chrome.webview.addEventListener("message", async event => {
            const reply = status => window.chrome.webview.postMessage(`CTS:${status}`);
            const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
            try {
              let dropArea = null;
              for (let attempt = 0; attempt < 100 && !dropArea; attempt++) {
                const candidate = document.querySelector('div[data-ved][jsname="QdEQIc"]');
                if (candidate?.offsetParent) dropArea = candidate;
                if (!dropArea) await sleep(100);
              }
              if (!dropArea) return reply("drop-area-missing");

              const binary = atob(event.data);
              const bytes = new Uint8Array(binary.length);
              for (let index = 0; index < binary.length; index++) {
                bytes[index] = binary.charCodeAt(index);
              }
              const fileName = "circle-to-search.png";
              const transfer = new DataTransfer();
              transfer.items.add(new File([bytes], fileName, { type: "image/png" }));

              for (const type of ["dragenter", "dragover", "drop"]) {
                dropArea.dispatchEvent(new DragEvent(type, {
                  bubbles: true,
                  cancelable: true,
                  dataTransfer: transfer
                }));
              }
              reply("submitted");
            } catch (error) {
              reply("script-error");
            }
          }, { once: true });
          return "ready";
        })()
        """;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dispatcher.Send(() =>
        {
            _window?.Close();
            _webView?.Dispose();
            _window = null;
            _webView = null;
        });
        _dispatcher.Dispose();
    }
}
