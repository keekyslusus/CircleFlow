using System.Net;
using System.Windows;
using System.Windows.Controls;
using CircleToSearch.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CircleToSearch.Capture;

internal interface ITraceVideoPreview : IDisposable
{
    FrameworkElement Root { get; }
    Task<bool> Ready { get; }
}

internal sealed class TraceVideoPreview : ITraceVideoPreview
{
    private readonly Func<Task<CoreWebView2Environment>> _environment;
    private readonly PluginLog _log;
    private readonly Uri _video;
    private bool _disposed;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TraceVideoPreview(Uri video, Func<Task<CoreWebView2Environment>> environment, PluginLog log)
    {
        _video = video;
        _environment = environment;
        _log = log;
        var transparent = PluginPalette.Transparent;
        View = new WebView2CompositionControl
        {
            IsHitTestVisible = false, Focusable = false,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(transparent.A, transparent.R, transparent.G, transparent.B),
        };
        Root.Children.Add(View);
        View.Loaded += OnLoaded;
    }

    public Task<bool> Ready => _ready.Task;
    FrameworkElement ITraceVideoPreview.Root => Root;
    internal Grid Root { get; } = new() { Opacity = 0 };
    internal WebView2CompositionControl View { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        View.Loaded -= OnLoaded;
        try
        {
            var environment = await _environment();
            if (_disposed) return;
            await View.EnsureCoreWebView2Async(environment);
            if (_disposed) return;
            var core = View.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.IsMuted = true;
            core.NavigationStarting += (_, args) => { if (args.IsUserInitiated) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.ProcessFailed += (_, _) => { if (!_disposed) Root.Opacity = 0; _ready.TrySetResult(false); };
            core.NavigationCompleted += (_, args) => { if (!args.IsSuccess) _ready.TrySetResult(false); };
            core.WebMessageReceived += (_, args) =>
            {
                if (_disposed || args.Source != "about:blank") return;
                var message = args.TryGetWebMessageAsString();
                if (message == "ready") { Root.Opacity = 1; _ready.TrySetResult(true); }
                else if (message == "failed") _ready.TrySetResult(false);
            };
            core.NavigateToString(BuildDocument(_video));
        }
        catch (Exception exception)
        {
            if (!_disposed) Root.Opacity = 0;
            _ready.TrySetResult(false);
            _log.Warn(nameof(TraceVideoPreview), $"video preview unavailable: {exception.Message}");
        }
    }

    internal static string BuildDocument(Uri video) => $$"""
        <!doctype html><html><head><meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; media-src https://api.trace.moe; style-src 'unsafe-inline'; script-src 'nonce-trace-preview'">
        <style>html,body{margin:0;width:100%;height:100%;overflow:hidden}video{width:100%;height:100%;object-fit:cover}</style>
        </head><body><video autoplay loop muted playsinline disablepictureinpicture src="{{WebUtility.HtmlEncode(video.AbsoluteUri)}}"></video>
        <script nonce="trace-preview">
        const video = document.querySelector('video');
        video.addEventListener('error', () => chrome.webview.postMessage('failed'), {once:true});
        video.addEventListener('playing', () => {
            const ready = () => chrome.webview.postMessage('ready');
            if (video.requestVideoFrameCallback) video.requestVideoFrameCallback(ready);
            else requestAnimationFrame(() => requestAnimationFrame(ready));
        }, {once:true});
        if (video.error) chrome.webview.postMessage('failed');
        </script></body></html>
        """;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ready.TrySetResult(false);
        View.Loaded -= OnLoaded;
        View.Dispose();
    }
}
