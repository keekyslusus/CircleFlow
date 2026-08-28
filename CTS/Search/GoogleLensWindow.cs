using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace CircleToSearch.Search;

public sealed class GoogleLensWindow : IDisposable
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
    private Grid? _loadingOverlay;
    private int _loadingGeneration;
    private bool _disposed;

    public GoogleLensWindow(string pluginDirectory, string userDataFolder, PluginLog log)
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

    public async Task<GoogleLensSearchStatus> ShowAsync(byte[] png, CancellationToken cancel)
    {
        if (_disposed) return GoogleLensSearchStatus.Failed;
        if (cancel.IsCancellationRequested) return GoogleLensSearchStatus.Canceled;

        var completion = new TaskCompletionSource<GoogleLensSearchStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryPost(() => _ = ShowOnUiThreadAsync(png, cancel, completion)))
            return GoogleLensSearchStatus.Failed;

        return await completion.Task.ConfigureAwait(false);
    }

    private async Task ShowOnUiThreadAsync(
        byte[] png,
        CancellationToken cancel,
        TaskCompletionSource<GoogleLensSearchStatus> completion)
    {
        try
        {
            cancel.ThrowIfCancellationRequested();
            await EnsureWindowAsync().ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();

            var window = _window!;
            var webView = _webView!;
            ShowLoadingOverlay();
            window.Show();
            window.Activate();

            await NavigateAsync(webView, GoogleLensHome, cancel).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();

            var resultsReady = await OpenLensResultsAsync(webView, window, png, cancel)
                .ConfigureAwait(true);
            HideLoadingOverlay();
            completion.TrySetResult(resultsReady
                ? GoogleLensSearchStatus.ResultsReady
                : GoogleLensSearchStatus.Failed);
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            HideLoadingOverlay();
            _log.Error(nameof(GoogleLensWindow), "WebView2 Runtime is unavailable", exception);
            completion.TrySetResult(GoogleLensSearchStatus.RuntimeUnavailable);
        }
        catch (OperationCanceledException)
        {
            HideLoadingOverlay();
            completion.TrySetResult(GoogleLensSearchStatus.Canceled);
        }
        catch (Exception exception)
        {
            HideLoadingOverlay();
            _log.Error(nameof(GoogleLensWindow), "opening Google Lens failed", exception);
            completion.TrySetResult(GoogleLensSearchStatus.Failed);
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

        var webView = new WebView2 { Visibility = Visibility.Hidden };
        var loadingOverlay = CreateLoadingOverlay();
        var content = new Grid();
        content.Children.Add(webView);
        content.Children.Add(loadingOverlay);
        var closed = false;
        var window = new Window
        {
            Title = "Circle to Search — Google Lens",
            Width = 1200,
            Height = 820,
            MinWidth = 720,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = content,
        };
        window.Closed += (_, _) =>
        {
            closed = true;
            webView.Dispose();
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                _webView = null;
                _loadingOverlay = null;
            }
        };
        window.PreviewKeyDown += OnWindowPreviewKeyDown;

        _window = window;
        _webView = webView;
        _loadingOverlay = loadingOverlay;

        try
        {
            window.Show();
            await webView.EnsureCoreWebView2Async(_environment).ConfigureAwait(true);
            if (closed) throw new OperationCanceledException();
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
        }
        catch
        {
            var wasClosed = closed;
            if (!closed) window.Close();
            if (wasClosed) throw new OperationCanceledException();
            throw;
        }
    }

    private static void OnWindowPreviewKeyDown(object sender, KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key != Key.W || (args.KeyboardDevice.Modifiers & ModifierKeys.Control) == 0) return;

        args.Handled = true;
        ((Window)sender).Close();
    }

    private static async Task NavigateAsync(WebView2 webView, Uri target, CancellationToken cancel)
    {
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess || args.WebErrorStatus != CoreWebView2WebErrorStatus.ConnectionAborted)
                completion.TrySetResult(args);
        }

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
        {
            if (args.IsSuccess || args.WebErrorStatus != CoreWebView2WebErrorStatus.ConnectionAborted)
                navigation.TrySetResult(args);
        }

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
                _log.Warn(nameof(GoogleLensWindow), "Google Lens upload acknowledgement timed out");
                return false;
            }

            var message = await acknowledgement.Task.ConfigureAwait(true);
            _log.Info(nameof(GoogleLensWindow), $"Google Lens upload response: {message}");
            if (!string.Equals(message, "CTS:submitted", StringComparison.Ordinal)) return false;

            var navigated = await Task.WhenAny(
                    navigation.Task,
                    closed.Task,
                    Task.Delay(NavigationTimeout, cancel))
                .ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (navigated != navigation.Task)
            {
                _log.Warn(nameof(GoogleLensWindow), "Google Lens results navigation timed out");
                return false;
            }

            var result = await navigation.Task.ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                _log.Warn(
                    nameof(GoogleLensWindow),
                    $"Google Lens results navigation failed: {result.WebErrorStatus}");
                return false;
            }

            _log.Info(nameof(GoogleLensWindow), "Google Lens results opened");
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

    private static Grid CreateLoadingOverlay()
    {
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var colors = new[] { "#4285F4", "#A142F4", "#0B57D0" };
        for (var index = 0; index < colors.Length; index++)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(5),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index])),
            };
            dot.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(520))
                {
                    AutoReverse = true,
                    BeginTime = TimeSpan.FromMilliseconds(index * 140),
                    RepeatBehavior = RepeatBehavior.Forever,
                });
            dots.Children.Add(dot);
        }

        var text = new TextBlock
        {
            Text = "Ищем с помощью Google Lens…",
            Margin = new Thickness(0, 18, 0, 0),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(48, 52, 58)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        center.Children.Add(dots);
        center.Children.Add(text);

        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(247, 249, 252)),
            IsHitTestVisible = true,
        };
        overlay.Children.Add(center);
        Panel.SetZIndex(overlay, 1);
        return overlay;
    }

    private void ShowLoadingOverlay()
    {
        var overlay = _loadingOverlay;
        if (overlay is null) return;
        _loadingGeneration++;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Opacity = 1;
        overlay.Visibility = Visibility.Visible;
        if (_webView is not null) _webView.Visibility = Visibility.Hidden;
    }

    private void HideLoadingOverlay()
    {
        var overlay = _loadingOverlay;
        if (overlay is null || overlay.Visibility != Visibility.Visible) return;
        var generation = _loadingGeneration;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_loadingOverlay, overlay) || generation != _loadingGeneration) return;
            if (_webView is not null) _webView.Visibility = Visibility.Visible;
            overlay.Visibility = Visibility.Collapsed;
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            overlay.Opacity = 1;
        };
        overlay.BeginAnimation(UIElement.OpacityProperty, fade);
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
