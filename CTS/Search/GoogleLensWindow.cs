using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Interop;
using CircleToSearch.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DrawingColor = System.Drawing.Color;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace CircleToSearch.Search;

public sealed class GoogleLensWindow : IDisposable
{
    private static readonly Uri GoogleLensHome = new("https://lens.google.com/?hl=en");
    private static readonly Uri GoogleLensUpload = new("https://lens.google.com/v3/upload");
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AttachmentTimeout = TimeSpan.FromSeconds(15);

    private readonly string _pluginDirectory;
    private readonly string _userDataFolder;
    private readonly UiStrings _strings;
    private readonly PluginLog _log;
    private readonly IStaDispatcher _dispatcher;
    private CoreWebView2Environment? _environment;
    private Window? _window;
    private WebView2? _webView;
    private Grid? _loadingOverlay;
    private TextBlock? _loadingText;
    private int _loadingGeneration;
    private bool _disposed;

    internal GoogleLensWindow(
        string pluginDirectory,
        string userDataFolder,
        UiStrings strings,
        PluginLog log,
        IStaDispatcher dispatcher)
    {
        _pluginDirectory = pluginDirectory;
        _userDataFolder = userDataFolder;
        _strings = strings;
        _log = log;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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
            ApplyTheme(SystemTheme.IsLight());
            ShowLoadingOverlay();
            window.Show();
            window.Activate();

            var resultsReady = await OpenLensResultsWithUploadAsync(webView, window, png, cancel)
                .ConfigureAwait(true);
            if (!resultsReady && ReferenceEquals(_window, window))
            {
                _log.Info(nameof(GoogleLensWindow), "falling back to Google Lens page upload");
                await NavigateAsync(webView, GoogleLensHome, cancel).ConfigureAwait(true);
                cancel.ThrowIfCancellationRequested();
                resultsReady = await OpenLensResultsWithScriptAsync(webView, window, png, cancel)
                    .ConfigureAwait(true);
            }

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

        var lightTheme = SystemTheme.IsLight();
        var palette = PluginPalette.For(lightTheme);
        var background = Frozen(palette.WindowSurface);
        var webViewBackground = ToDrawingColor(palette.WindowSurface);
        var webView = new WebView2
        {
            Visibility = Visibility.Hidden,
            DefaultBackgroundColor = webViewBackground,
        };
        var loadingOverlay = CreateLoadingOverlay(palette, out var loadingText);
        var content = new Grid { Background = background };
        content.Children.Add(webView);
        content.Children.Add(loadingOverlay);
        var closed = false;
        var window = new Window
        {
            Title = _strings.GoogleLensWindowTitle,
            Width = 1200,
            Height = 820,
            MinWidth = 720,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = background,
            Content = content,
        };
        window.SourceInitialized += (_, _) => ApplyWindowChromeTheme(window, lightTheme);
        window.Closed += (_, _) =>
        {
            closed = true;
            webView.Dispose();
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                _webView = null;
                _loadingOverlay = null;
                _loadingText = null;
            }
        };
        window.PreviewKeyDown += OnWindowPreviewKeyDown;

        _window = window;
        _webView = webView;
        _loadingOverlay = loadingOverlay;
        _loadingText = loadingText;

        try
        {
            window.Show();
            var controllerOptions = _environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.DefaultBackgroundColor = webViewBackground;
            await webView.EnsureCoreWebView2Async(_environment, controllerOptions).ConfigureAwait(true);
            if (closed) throw new OperationCanceledException();
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            ApplyTheme(lightTheme);
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

    private async Task<bool> OpenLensResultsWithUploadAsync(
        WebView2 webView,
        Window window,
        byte[] png,
        CancellationToken cancel)
    {
        var navigation = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnClosed(object? sender, EventArgs args) => closed.TrySetResult();
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess || args.WebErrorStatus != CoreWebView2WebErrorStatus.ConnectionAborted)
                navigation.TrySetResult(args);
        }

        webView.NavigationCompleted += OnNavigationCompleted;
        window.Closed += OnClosed;
        try
        {
            var boundary = $"----CircleToSearch{Guid.NewGuid():N}";
            using var body = CreateLensUploadBody(png, boundary);
            var request = _environment!.CreateWebResourceRequest(
                GoogleLensUpload.AbsoluteUri,
                "POST",
                body,
                CreateLensUploadHeaders(boundary));
            webView.CoreWebView2.NavigateWithWebResourceRequest(request);

            var finished = await Task.WhenAny(
                    navigation.Task,
                    closed.Task,
                    Task.Delay(NavigationTimeout, cancel))
                .ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (finished != navigation.Task)
            {
                if (finished != closed.Task)
                    _log.Warn(nameof(GoogleLensWindow), "Google Lens direct upload navigation timed out");
                return false;
            }

            var result = await navigation.Task.ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                _log.Warn(
                    nameof(GoogleLensWindow),
                    $"Google Lens direct upload navigation failed: {result.WebErrorStatus}");
                return false;
            }

            if (!IsGoogleLensResultsUrl(webView.Source))
            {
                _log.Warn(
                    nameof(GoogleLensWindow),
                    $"Google Lens direct upload returned an unexpected URL: {webView.Source}");
                return false;
            }

            _log.Info(nameof(GoogleLensWindow), "Google Lens results opened through direct upload");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Warn(
                nameof(GoogleLensWindow),
                $"Google Lens direct upload failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            webView.NavigationCompleted -= OnNavigationCompleted;
            window.Closed -= OnClosed;
        }
    }

    internal static MemoryStream CreateLensUploadBody(byte[] png, string boundary)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        if (boundary.Contains('\r', StringComparison.Ordinal) ||
            boundary.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("Multipart boundary cannot contain line breaks.", nameof(boundary));
        }

        var stream = new MemoryStream();
        WriteAscii(
            stream,
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"encoded_image\"; filename=\"circle-to-search.png\"\r\n" +
            "Content-Type: image/png\r\n\r\n");
        stream.Write(png);
        WriteAscii(stream, $"\r\n--{boundary}--\r\n");
        stream.Position = 0;
        return stream;
    }

    internal static string CreateLensUploadHeaders(string boundary) =>
        $"Content-Type: multipart/form-data; boundary={boundary}";

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }

    private async Task<bool> OpenLensResultsWithScriptAsync(
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

    private Grid CreateLoadingOverlay(
        PluginThemePalette palette,
        out TextBlock loadingText)
    {
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (var index = 0; index < PluginPalette.GoogleLensLoadingDots.Count; index++)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(5),
                Fill = Frozen(PluginPalette.GoogleLensLoadingDots[index]),
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

        loadingText = new TextBlock
        {
            Text = _strings.GoogleLensLoading,
            Margin = new Thickness(0, 18, 0, 0),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 16,
            Foreground = Frozen(palette.PrimaryText),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        center.Children.Add(dots);
        center.Children.Add(loadingText);

        var overlay = new Grid
        {
            Background = Frozen(palette.WindowSurface),
            IsHitTestVisible = true,
        };
        overlay.Children.Add(center);
        Panel.SetZIndex(overlay, 1);
        return overlay;
    }

    private void ApplyTheme(bool lightTheme)
    {
        var palette = PluginPalette.For(lightTheme);
        var background = Frozen(palette.WindowSurface);
        if (_window is not null)
        {
            _window.Background = background;
            if (_window.Content is Panel content) content.Background = background;
            ApplyWindowChromeTheme(_window, lightTheme);
        }

        if (_loadingOverlay is not null) _loadingOverlay.Background = background;
        if (_loadingText is not null) _loadingText.Foreground = Frozen(palette.PrimaryText);
        if (_webView is not null)
        {
            _webView.DefaultBackgroundColor = ToDrawingColor(palette.WindowSurface);
            if (_webView.CoreWebView2 is { } coreWebView)
            {
                coreWebView.Profile.PreferredColorScheme = lightTheme
                    ? CoreWebView2PreferredColorScheme.Light
                    : CoreWebView2PreferredColorScheme.Dark;
            }
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DrawingColor ToDrawingColor(Color color) =>
        DrawingColor.FromArgb(color.A, color.R, color.G, color.B);

    private static void ApplyWindowChromeTheme(Window window, bool lightTheme)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var useDarkMode = lightTheme ? 0 : 1;
        var result = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref useDarkMode,
            sizeof(int));
        if (result != 0)
        {
            NativeMethods.DwmSetWindowAttribute(
                hwnd,
                NativeMethods.DwmwaUseImmersiveDarkModeBefore20H1,
                ref useDarkMode,
                sizeof(int));
        }
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
