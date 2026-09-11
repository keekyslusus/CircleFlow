using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CircleToSearch.Interop;
using Microsoft.Web.WebView2.Core;

namespace CircleToSearch.Translation;

internal sealed class GoogleImageTranslationSigner(
    HttpClient http, IStaDispatcher dispatcher, string profileDirectory,
    TranslationMemoryProfiler? profiler = null) : IImageTranslationSigner, IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Window? _window;
    private CoreWebView2? _browser;
    private CoreWebView2Controller? _controller;
    private GoogleImageChallenge? _challenge;
    private DateTime _initialized;
    private DispatcherTimer? _idle;
    private int _disposed;
    private Action? _stopTracking;
    private readonly object _stopGate = new();
    private readonly object _operationGate = new();
    private Task? _stopTask;
    private SignOperation? _activeOperation;

    public async Task<ImageTranslationSignature> SignAsync(string request, string target, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var operation = new SignOperation();
            lock (_operationGate)
            {
                if (_disposed != 0) throw new OperationCanceledException(_lifetime.Token);
                _activeOperation = operation;
            }
            if (!dispatcher.TryPost(async () =>
                {
                    if (!operation.TryStart()) return;
                    try { operation.SetResult(await SignOnUiAsync(request, target, linked.Token)); }
                    catch (OperationCanceledException) { Reset(); operation.SetCanceled(linked.Token); }
                    catch (Exception error) { Reset(); operation.SetException(error); }
                    finally { operation.Finish(); }
                }))
            {
                operation.SetException(new InvalidOperationException("Image translation dispatcher is unavailable."));
                operation.Finish();
            }
            try { return await operation.Result.ConfigureAwait(false); }
            finally
            {
                lock (_operationGate)
                {
                    if (ReferenceEquals(_activeOperation, operation)) _activeOperation = null;
                }
            }
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
            _window = new Window { Width = 64, Height = 64,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            // WPF opacity does not hide WebView2's native child window. Never show the host HWND.
            var handle = new WindowInteropHelper(_window).EnsureHandle();
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileDirectory).WaitAsync(cancellation);
            void TrackProcesses()
            {
                try { profiler?.TrackProcesses(environment.GetProcessInfos().Select(info => (info.ProcessId, info.Kind.ToString()))); }
                catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            }
            EventHandler<object> processChanged = (_, _) => TrackProcesses();
            environment.ProcessInfosChanged += processChanged;
            _stopTracking = () => environment.ProcessInfosChanged -= processChanged;
            _controller = await environment.CreateCoreWebView2ControllerAsync(handle);
            _controller.IsVisible = false;
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, 64, 64);
            TrackProcesses();
            profiler?.Mark("webview_created");
            cancellation.ThrowIfCancellationRequested();
            var browser = _controller.CoreWebView2;
            _browser = browser;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            browser.NewWindowRequested += (_, args) => args.Handled = true;
            browser.DownloadStarting += (_, args) => args.Cancel = true;
            browser.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            browser.ProcessFailed += (_, _) => _challenge = null;
            browser.NavigationStarting += (_, args) =>
            {
                if (!string.Equals(args.Uri, GoogleImageTranslationProtocol.FrameUrl, StringComparison.Ordinal)) args.Cancel = true;
            };
            using var initialization = new HttpRequestMessage(HttpMethod.Get,
                GoogleImageTranslationProtocol.Origin + "/?sl=auto&tl=" + Uri.EscapeDataString(target) + "&op=images");
            initialization.Headers.TryAddWithoutValidation("User-Agent", browser.Settings.UserAgent);
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
                browser.Navigate(GoogleImageTranslationProtocol.FrameUrl);
                await navigation.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
            }
            finally { browser.NavigationCompleted -= Navigated; }
            await browser.ExecuteScriptAsync(challenge.Interpreter).WaitAsync(cancellation);
            await browser.ExecuteScriptAsync("window.ctsGenerator = new window.ridgeslice.gc(" + JsonSerializer.Serialize(challenge.Program) + ");")
                .WaitAsync(cancellation);
            _challenge = challenge;
            _initialized = DateTime.UtcNow;
            TrackProcesses();
            profiler?.Mark("webview_ready");
        }
        var expression = "new Promise((resolve,reject)=>{const timeout=setTimeout(()=>reject(new Error('Generator timeout')),15000);" +
            "try{const started=performance.now();window.ctsGenerator.ply(token=>{clearTimeout(timeout);" +
            "resolve({token,ms:performance.now()-started});},true,{mgGpzd:" + JsonSerializer.Serialize(request) +
            "});}catch(e){clearTimeout(timeout);reject(e);}})";
        var evaluation = await _browser.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true })).WaitAsync(cancellation);
        using var json = JsonDocument.Parse(evaluation);
        if (json.RootElement.TryGetProperty("exceptionDetails", out _)) throw new InvalidDataException("Image translation signing failed.");
        var value = json.RootElement.GetProperty("result").GetProperty("value");
        var header = JsonSerializer.Serialize(new object?[] { value.GetProperty("token").GetString(), null, null,
            (int)value.GetProperty("ms").GetDouble(), 0, null, 0, 0, _challenge?.State });
        _idle ??= new DispatcherTimer(TimeSpan.FromMinutes(2), DispatcherPriority.Background, (_, _) => Reset(), Dispatcher.CurrentDispatcher);
        _idle.Start();
        return new(header, _browser.Settings.UserAgent);
    }

    private void Reset()
    {
        var hadController = _controller is not null;
        _stopTracking?.Invoke();
        _stopTracking = null;
        _idle?.Stop();
        _challenge = null;
        _controller?.Close();
        _controller = null;
        _browser = null;
        _window?.Close();
        _window = null;
        if (hadController) profiler?.Mark("webview_closed");
    }

    public void Dispose()
    {
        try { StopAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        catch (TimeoutException) { }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    public Task StopAsync()
    {
        TaskCompletionSource completion;
        lock (_stopGate)
        {
            if (_stopTask is not null) return _stopTask;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = completion.Task;
            Interlocked.Exchange(ref _disposed, 1);
        }
        Exception? cancellationFailure = null;
        try { _lifetime.Cancel(); }
        catch (Exception exception) { cancellationFailure = exception; }
        lock (_operationGate) _activeOperation?.CancelBeforeStart(_lifetime.Token);
        _ = CompleteStopAsync(completion, cancellationFailure);
        return completion.Task;
    }

    private async Task CompleteStopAsync(TaskCompletionSource completion, Exception? cancellationFailure)
    {
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            if (cancellationFailure is not null) throw cancellationFailure;
            completion.TrySetResult();
        }
        catch (Exception exception) { completion.TrySetException(exception); }
    }

    private async Task StopCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        Exception? resetFailure = null;
        try
        {
            var reset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcher.TryPost(() =>
                {
                    try { Reset(); }
                    catch (Exception exception) { resetFailure = exception; }
                    finally { reset.TrySetResult(); }
                }))
            {
                reset.TrySetResult();
            }
            await reset.Task.ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        await dispatcher.StopAsync().ConfigureAwait(false);
        if (resetFailure is not null) throw resetFailure;
    }

    private sealed class SignOperation
    {
        private readonly TaskCompletionSource<ImageTranslationSignature> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _state;

        public Task<ImageTranslationSignature> Result => _result.Task;

        public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        public void SetResult(ImageTranslationSignature result) => _result.TrySetResult(result);

        public void SetCanceled(CancellationToken cancellationToken) => _result.TrySetCanceled(cancellationToken);

        public void SetException(Exception exception) => _result.TrySetException(exception);

        public void Finish()
        {
            Interlocked.Exchange(ref _state, 2);
            if (!_result.Task.IsCompleted)
                _result.TrySetException(new InvalidOperationException("Image translation signing ended without a result."));
        }

        public bool CancelBeforeStart(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) != 0) return false;
            _result.TrySetCanceled(cancellationToken);
            return true;
        }
    }
}
