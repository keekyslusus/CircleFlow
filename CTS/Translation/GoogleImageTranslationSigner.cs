using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CircleToSearch.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CircleToSearch.Translation;

internal sealed class GoogleImageTranslationSigner(
    HttpClient http, IStaDispatcher dispatcher, string profileDirectory) : IImageTranslationSigner, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Window? _window;
    private WebView2? _browser;
    private GoogleImageChallenge? _challenge;
    private DateTime _initialized;
    private DispatcherTimer? _idle;
    private int _disposed;

    public async Task<ImageTranslationSignature> SignAsync(string request, string target, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var completion = new TaskCompletionSource<ImageTranslationSignature>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcher.TryPost(async () =>
                {
                    try { completion.TrySetResult(await SignOnUiAsync(request, target, linked.Token)); }
                    catch (OperationCanceledException) { Reset(); completion.TrySetCanceled(linked.Token); }
                    catch (Exception error) { Reset(); completion.TrySetException(error); }
                })) throw new InvalidOperationException("Image translation dispatcher is unavailable.");
            return await completion.Task.ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<ImageTranslationSignature> SignOnUiAsync(string request, string target, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        _idle?.Stop();
        if (_browser is null || _challenge is null || DateTime.UtcNow - _initialized > TimeSpan.FromMinutes(5))
        {
            Reset();
            var browser = new WebView2();
            _browser = browser;
            _window = new Window { Width = 64, Height = 64, Content = browser, Opacity = 0,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            _window.Show();
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileDirectory).WaitAsync(cancellation);
            await browser.EnsureCoreWebView2Async(environment).WaitAsync(cancellation);
            browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            browser.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
            browser.CoreWebView2.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            browser.CoreWebView2.ProcessFailed += (_, _) => _challenge = null;
            browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!string.Equals(args.Uri, GoogleImageTranslationProtocol.FrameUrl, StringComparison.Ordinal)) args.Cancel = true;
            };
            using var initialization = new HttpRequestMessage(HttpMethod.Get,
                GoogleImageTranslationProtocol.Origin + "/?sl=auto&tl=" + Uri.EscapeDataString(target) + "&op=images");
            initialization.Headers.TryAddWithoutValidation("User-Agent", browser.CoreWebView2.Settings.UserAgent);
            using var response = await http.SendAsync(initialization, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            var html = await GoogleImageTranslationProtocol.ReadBoundedAsync(response.Content, 4 * 1024 * 1024, cancellation);
            var challenge = GoogleImageTranslationProtocol.ReadChallenge(html);
            var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Navigated(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (args.IsSuccess) navigation.TrySetResult();
                else navigation.TrySetException(new IOException("Image translation frame failed to load."));
            }
            browser.NavigationCompleted += Navigated;
            try
            {
                browser.CoreWebView2.Navigate(GoogleImageTranslationProtocol.FrameUrl);
                await navigation.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
            }
            finally { browser.NavigationCompleted -= Navigated; }
            await browser.ExecuteScriptAsync(challenge.Interpreter).WaitAsync(cancellation);
            await browser.ExecuteScriptAsync("window.ctsGenerator = new window.ridgeslice.gc(" + JsonSerializer.Serialize(challenge.Program) + ");")
                .WaitAsync(cancellation);
            _challenge = challenge;
            _initialized = DateTime.UtcNow;
        }
        var expression = "new Promise((resolve,reject)=>{const timeout=setTimeout(()=>reject(new Error('Generator timeout')),15000);" +
            "try{const started=performance.now();window.ctsGenerator.ply(token=>{clearTimeout(timeout);" +
            "resolve({token,ms:performance.now()-started});},true,{mgGpzd:" + JsonSerializer.Serialize(request) +
            "});}catch(e){clearTimeout(timeout);reject(e);}})";
        var evaluation = await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true })).WaitAsync(cancellation);
        using var json = JsonDocument.Parse(evaluation);
        if (json.RootElement.TryGetProperty("exceptionDetails", out _)) throw new InvalidDataException("Image translation signing failed.");
        var value = json.RootElement.GetProperty("result").GetProperty("value");
        var header = JsonSerializer.Serialize(new object?[] { value.GetProperty("token").GetString(), null, null,
            (int)value.GetProperty("ms").GetDouble(), 0, null, 0, 0, _challenge?.State });
        _idle ??= new DispatcherTimer(TimeSpan.FromMinutes(2), DispatcherPriority.Background, (_, _) => Reset(), Dispatcher.CurrentDispatcher);
        _idle.Start();
        return new(header, _browser.CoreWebView2.Settings.UserAgent);
    }

    private void Reset()
    {
        _idle?.Stop();
        _challenge = null;
        _browser?.Dispose();
        _browser = null;
        _window?.Close();
        _window = null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        dispatcher.Send(Reset);
        dispatcher.Dispose();
    }
}
