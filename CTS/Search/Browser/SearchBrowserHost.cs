using System.Windows;
using System.Windows.Input;
using CircleToSearch.Interop;
using CircleToSearch.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DrawingColor = System.Drawing.Color;

namespace CircleToSearch.Search.Browser;

public sealed class SearchBrowserHost : ISearchBrowserHost, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly string _assetDirectory;
    private readonly string _userDataFolder;
    private readonly PluginLog _log;
    private readonly IStaDispatcher _dispatcher;
    private readonly Func<Task<CoreWebView2Environment>> _createEnvironment;
    private readonly Func<FrameworkElement, POINT, bool, SearchBrowserWindowView> _createWindowView;
    private readonly TimeSpan _shutdownTimeout;
    private readonly SemaphoreSlim _showGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _lifecycleGate = new();
    private CoreWebView2Environment? _environment;
    private SearchBrowserWindowView? _view;
    private WebView2? _webView;
    private CancellationTokenSource? _windowLifetime;
    private Action? _cleanupView;
    private long _showGeneration;
    private SearchBrowserUiOperation? _activeOperation;
    private (CancellationTokenSource Show, Task Reveal)? _hiddenShow;
    private int _disposed;
    private Task? _stopTask;

    internal SearchBrowserHost(
        string assetDirectory,
        string userDataFolder,
        PluginLog log,
        IStaDispatcher dispatcher,
        Func<Task<CoreWebView2Environment>> createEnvironment,
        Func<FrameworkElement, POINT, bool, SearchBrowserWindowView> createWindowView,
        TimeSpan? shutdownTimeout = null)
    {
        _assetDirectory = assetDirectory;
        _userDataFolder = userDataFolder;
        _log = log;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _createEnvironment = createEnvironment ?? throw new ArgumentNullException(nameof(createEnvironment));
        _createWindowView = createWindowView ?? throw new ArgumentNullException(nameof(createWindowView));
        _shutdownTimeout = shutdownTimeout ?? ShutdownTimeout;
        if (_shutdownTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
    }

    public static string? GetRuntimeVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            return null;
        }
    }

    public async Task<SearchBrowserShowResult> ShowAsync(
        SearchProviderDescriptor descriptor,
        PreparedVisualSearch preparedSearch,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(preparedSearch);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, _lifetime.Token);
        lock (_lifecycleGate)
        {
            // A show still hidden behind its reveal was never committed by the user, so a newer show replaces it.
            if (_hiddenShow is { Reveal.IsCompleted: false } replaced) replaced.Show.Cancel();
            _hiddenShow = preparedSearch.RevealAfter is { IsCompleted: false } reveal ? (linked, reveal) : null;
        }
        try
        {
            return await ShowWhenGateOpensAsync(descriptor, preparedSearch, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_lifecycleGate)
            {
                if (_hiddenShow?.Show == linked) _hiddenShow = null;
            }
        }
    }

    private async Task<SearchBrowserShowResult> ShowWhenGateOpensAsync(
        SearchProviderDescriptor descriptor,
        PreparedVisualSearch preparedSearch,
        CancellationToken cancel)
    {
        SearchBrowserUiOperation? activeOperation = null;
        try
        {
            await _showGate.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled);
        }

        try
        {
            if (cancel.IsCancellationRequested)
                return new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled);

            var operation = new SearchBrowserUiOperation();
            activeOperation = operation;
            lock (_lifecycleGate)
            {
                if (_disposed != 0)
                    return new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled);
                _activeOperation = operation;
            }
            NativeMethods.GetCursorPos(out var anchor);
            if (!_dispatcher.TryPost(() =>
                {
                    if (operation.TryStart())
                        _ = ShowOnUiThreadAsync(descriptor, preparedSearch, cancel, operation, anchor);
                }))
            {
                operation.CancelBeforeStart();
                return new SearchBrowserShowResult(
                    _lifetime.IsCancellationRequested
                        ? SearchBrowserShowStatus.Canceled
                        : SearchBrowserShowStatus.InitializationFailed);
            }

            var result = await operation.Result.ConfigureAwait(false);
            return _lifetime.IsCancellationRequested
                ? new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled)
                : result;
        }
        finally
        {
            if (activeOperation is not null)
            {
                lock (_lifecycleGate)
                {
                    if (ReferenceEquals(_activeOperation, activeOperation))
                        _activeOperation = null;
                }
            }
            _showGate.Release();
        }
    }

    private async Task ShowOnUiThreadAsync(
        SearchProviderDescriptor descriptor,
        PreparedVisualSearch preparedSearch,
        CancellationToken cancel,
        SearchBrowserUiOperation operation,
        POINT anchor)
    {
        var generation = ++_showGeneration;
        var reveal = preparedSearch.RevealAfter is { IsCompleted: false } pending ? pending : null;
        try
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                if (reveal is not null && _view is not null)
                {
                    // An open results window belongs to the user until they submit; its browser is already warm.
                    await reveal.WaitAsync(cancel).ConfigureAwait(true);
                    reveal = null;
                }
                await EnsureWindowAsync(anchor, descriptor, hidden: reveal is not null, cancel).ConfigureAwait(true);
                cancel.ThrowIfCancellationRequested();
            }
            catch (WebView2RuntimeNotFoundException exception)
            {
                _log.Error(nameof(SearchBrowserHost), "WebView2 Runtime is unavailable", exception);
                CloseFailedShow();
                operation.SetOutcome(new SearchBrowserShowResult(
                    SearchBrowserShowStatus.RuntimeUnavailable));
                return;
            }
            catch (OperationCanceledException)
            {
                operation.SetOutcome(new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled));
                return;
            }
            catch (Exception exception)
            {
                _log.Error(nameof(SearchBrowserHost), "initializing the search browser failed", exception);
                CloseFailedShow();
                operation.SetOutcome(new SearchBrowserShowResult(
                    SearchBrowserShowStatus.InitializationFailed));
                return;
            }

            var view = _view!;
            var window = view.Window;
            var webView = _webView!;
            using var showLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancel,
                _lifetime.Token,
                _windowLifetime!.Token);
            view.SetProvider(descriptor);
            var lightTheme = SystemTheme.IsLight();
            view.ApplyTheme(lightTheme);
            ApplyBrowserTheme(webView, lightTheme);
            view.ShowLoading();
            webView.CoreWebView2.Stop();
            if (reveal is null)
            {
                window.Show();
                window.Activate();
            }

            var session = new WebView2VisualSearchBrowserSession(
                webView,
                _environment!,
                () => generation == _showGeneration &&
                      ReferenceEquals(_view, view) &&
                      !showLifetime.IsCancellationRequested);
            SearchBrowserShowStatus status;
            try
            {
                if (preparedSearch.Kind == PreparedVisualSearchKind.Url)
                {
                    var navigation = await session.NavigateAsync(
                            preparedSearch.RequireResultsUrl(),
                            NavigationTimeout,
                            showLifetime.Token)
                        .ConfigureAwait(true);
                    status = navigation.Status switch
                    {
                        BrowserNavigationStatus.Succeeded => SearchBrowserShowStatus.Shown,
                        BrowserNavigationStatus.Canceled => SearchBrowserShowStatus.Canceled,
                        _ => SearchBrowserShowStatus.NavigationFailed,
                    };
                }
                else
                {
                    var execution = preparedSearch.RequireBrowserOperation()
                        .ExecuteAsync(session, showLifetime.Token);
                    if (reveal is not null)
                        await RevealAsync(view, reveal, execution, anchor, showLifetime.Token).ConfigureAwait(true);
                    var browserOperationStatus = await execution.ConfigureAwait(true);
                    status = browserOperationStatus switch
                    {
                        VisualSearchBrowserOperationStatus.Succeeded => SearchBrowserShowStatus.Shown,
                        VisualSearchBrowserOperationStatus.Canceled => SearchBrowserShowStatus.Canceled,
                        _ => SearchBrowserShowStatus.ProviderOperationFailed,
                    };
                }
            }
            catch (OperationCanceledException)
            {
                status = SearchBrowserShowStatus.Canceled;
            }
            catch (Exception exception)
            {
                var operationName = preparedSearch.Kind == PreparedVisualSearchKind.Url
                    ? "initial search navigation"
                    : "provider browser operation";
                _log.Error(nameof(SearchBrowserHost), $"{operationName} failed", exception);
                status = preparedSearch.Kind == PreparedVisualSearchKind.Url
                    ? SearchBrowserShowStatus.NavigationFailed
                    : SearchBrowserShowStatus.ProviderOperationFailed;
            }

            if (showLifetime.IsCancellationRequested || view.IsClosed ||
                !ReferenceEquals(_view, view))
                status = SearchBrowserShowStatus.Canceled;

            if (status == SearchBrowserShowStatus.Shown)
                view.HideLoading();
            else if (status != SearchBrowserShowStatus.Canceled || ReferenceEquals(_view, view))
                CloseFailedShow(view);
            operation.SetOutcome(new SearchBrowserShowResult(status));
        }
        catch (Exception exception)
        {
            _log.Error(nameof(SearchBrowserHost), "completing the search browser operation failed", exception);
            CloseFailedShow();
            operation.SetOutcome(new SearchBrowserShowResult(
                cancel.IsCancellationRequested
                    ? SearchBrowserShowStatus.Canceled
                    : SearchBrowserShowStatus.InitializationFailed));
        }
        finally
        {
            operation.Finish();
        }
    }

    private static async Task RevealAsync(
        SearchBrowserWindowView view,
        Task reveal,
        Task<VisualSearchBrowserOperationStatus> execution,
        POINT showAnchor,
        CancellationToken cancel)
    {
        // A failed operation closes the hidden window itself, so it is never revealed.
        if (await Task.WhenAny(reveal, execution).ConfigureAwait(true) != reveal &&
            !(execution.IsCompletedSuccessfully && execution.Result == VisualSearchBrowserOperationStatus.Succeeded))
            return;
        // Results can finish loading while the overlay that hides them is still closing.
        await reveal.WaitAsync(cancel).ConfigureAwait(true);
        if (cancel.IsCancellationRequested || view.IsClosed) return;
        // The pointer moved while the hidden browser loaded, so the window follows where it ended up.
        view.MoveTo(NativeMethods.GetCursorPos(out var pointer) ? pointer : showAnchor);
        _ = view.ShowAsync();
        view.Window.Activate();
    }

    private async Task EnsureWindowAsync(
        POINT anchor,
        SearchProviderDescriptor descriptor,
        bool hidden,
        CancellationToken cancel)
    {
        if (_view is not null && _webView is not null)
        {
            var existingView = _view;
            existingView.SetProvider(descriptor);
            existingView.MoveTo(anchor);
            await existingView.ShowAsync().ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (existingView.IsClosed || !ReferenceEquals(_view, existingView))
                throw new OperationCanceledException();
            return;
        }

        _environment ??= await _createEnvironment().ConfigureAwait(true);
        cancel.ThrowIfCancellationRequested();

        var lightTheme = SystemTheme.IsLight();
        var webViewBackground = ToDrawingColor(PluginPalette.For(lightTheme).WindowSurface);
        var webView = new WebView2 { DefaultBackgroundColor = webViewBackground };
        var windowLifetime = new CancellationTokenSource();
        SearchBrowserWindowView view;
        try
        {
            view = _createWindowView(webView, anchor, lightTheme)
                ?? throw new InvalidOperationException("The browser window view factory returned null.");
        }
        catch
        {
            webView.Dispose();
            windowLifetime.Dispose();
            throw;
        }

        var cleaned = false;
        CoreWebView2? browserCore = null;
        void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            // The runtime's default popup bypasses our window and document setup.
            args.Handled = true;
            if (cleaned || !ReferenceEquals(_view, view) ||
                !Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)) return;

            try { browserCore!.Navigate(target.AbsoluteUri); }
            catch (Exception exception)
            {
                _log.SafeError(nameof(SearchBrowserHost), "navigate-popup-in-browser", exception);
            }
        }
        void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            if (browserCore is not null)
            {
                WebView2VisualSearchBrowserSession.TryCleanup(
                    () => browserCore.NewWindowRequested -= OnNewWindowRequested);
                WebView2VisualSearchBrowserSession.TryCleanup(
                    () => browserCore.ContextMenuRequested -= OnContextMenuRequested);
            }
            try { windowLifetime.Cancel(); }
            catch (Exception exception)
            {
                _log.SafeError(nameof(SearchBrowserHost), "cancel-browser-window", exception);
            }
            try { webView.Dispose(); }
            catch (Exception exception)
            {
                _log.SafeError(nameof(SearchBrowserHost), "dispose-browser-view", exception);
            }
            finally
            {
                windowLifetime.Dispose();
                view.Window.Closed -= OnClosed;
                view.Window.PreviewKeyDown -= OnWindowPreviewKeyDown;
                if (ReferenceEquals(_view, view))
                {
                    _showGeneration++;
                    _view = null;
                    _webView = null;
                    _windowLifetime = null;
                    _cleanupView = null;
                }
            }
        }
        void OnClosed(object? sender, EventArgs args) => Cleanup();
        view.Window.Closed += OnClosed;
        view.Window.PreviewKeyDown += OnWindowPreviewKeyDown;
        _view = view;
        _webView = webView;
        _windowLifetime = windowLifetime;
        _cleanupView = Cleanup;

        try
        {
            view.SetProvider(descriptor);
            if (hidden) view.ShowHidden();
            else await view.ShowAsync().ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (view.IsClosed || !ReferenceEquals(_view, view)) throw new OperationCanceledException();
            var controllerOptions = _environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.DefaultBackgroundColor = webViewBackground;
            await webView.EnsureCoreWebView2Async(_environment, controllerOptions).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (view.IsClosed || !ReferenceEquals(_view, view)) throw new OperationCanceledException();
            browserCore = webView.CoreWebView2;
            browserCore.NewWindowRequested += OnNewWindowRequested;
            browserCore.ContextMenuRequested += OnContextMenuRequested;
            await webView.CoreWebView2
                .AddScriptToExecuteOnDocumentCreatedAsync(OverlayScrollbarScript.Create())
                .ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (view.IsClosed || !ReferenceEquals(_view, view)) throw new OperationCanceledException();
            var extensionDirectory = await Task.Run(() =>
                SearchBrowserExtension.Prepare(_assetDirectory, _userDataFolder), cancel).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (view.IsClosed || !ReferenceEquals(_view, view)) throw new OperationCanceledException();
            var extension = await webView.CoreWebView2.Profile
                .AddBrowserExtensionAsync(extensionDirectory).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (view.IsClosed || !ReferenceEquals(_view, view)) throw new OperationCanceledException();
            if (!extension.IsEnabled) await extension.EnableAsync(true).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (view.IsClosed || !ReferenceEquals(_view, view)) throw new OperationCanceledException();
            _log.Info(nameof(SearchBrowserHost), $"uBlock Origin Lite enabled: {extension.Id}");
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            view.ApplyTheme(lightTheme);
            ApplyBrowserTheme(webView, lightTheme);
        }
        catch
        {
            var wasClosed = view.IsClosed;
            if (!wasClosed)
            {
                try { view.Window.Close(); }
                finally { Cleanup(); }
            }
            if (wasClosed) throw new OperationCanceledException();
            throw;
        }
    }

    private static void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        for (var index = args.MenuItems.Count - 1; index >= 0; index--)
        {
            if (args.MenuItems[index].Name == "openLinkInNewWindow")
                args.MenuItems.RemoveAt(index);
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key == Key.Left &&
            (args.KeyboardDevice.Modifiers & ModifierKeys.Alt) != 0 &&
            _webView?.CoreWebView2 is { CanGoBack: true } core)
        {
            core.GoBack();
            args.Handled = true;
            return;
        }
        if (key != Key.Escape &&
            (key != Key.W || (args.KeyboardDevice.Modifiers & ModifierKeys.Control) == 0)) return;

        args.Handled = true;
        var window = (Window)sender;
        window.Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(_view?.Window, window)) window.Close();
        });
    }

    private static void ApplyBrowserTheme(WebView2 webView, bool lightTheme)
    {
        webView.DefaultBackgroundColor = ToDrawingColor(PluginPalette.For(lightTheme).WindowSurface);
        if (webView.CoreWebView2 is { } coreWebView)
            coreWebView.Profile.PreferredColorScheme = lightTheme
                ? CoreWebView2PreferredColorScheme.Light
                : CoreWebView2PreferredColorScheme.Dark;
    }

    private static DrawingColor ToDrawingColor(System.Windows.Media.Color color) =>
        DrawingColor.FromArgb(color.A, color.R, color.G, color.B);

    private void CloseFailedShow(SearchBrowserWindowView? expected = null)
    {
        var view = expected ?? _view;
        if (view is not null && ReferenceEquals(_view, view) && !view.IsClosed)
            view.Window.Close();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    public void Dispose()
    {
        var stop = StopAsync();
        try { stop.WaitAsync(_shutdownTimeout).GetAwaiter().GetResult(); }
        catch (TimeoutException)
        {
            _log.Warn(
                nameof(SearchBrowserHost),
                "browser UI operation did not stop within the shutdown timeout; dispatcher cleanup was deferred");
            _ = stop.ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        var aggregate = task.Exception!.Flatten();
                        var exception = aggregate.InnerExceptions.Count == 1
                            ? aggregate.InnerExceptions[0]
                            : aggregate;
                        _log.SafeError(nameof(SearchBrowserHost), "deferred-browser-cleanup", exception);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception exception)
        {
            _log.SafeError(nameof(SearchBrowserHost), "dispose", exception);
        }
    }

    public Task StopAsync()
    {
        TaskCompletionSource completion;
        SearchBrowserUiOperation? activeOperation;
        lock (_lifecycleGate)
        {
            if (_stopTask is not null) return _stopTask;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = completion.Task;
            Interlocked.Exchange(ref _disposed, 1);
            activeOperation = _activeOperation;
        }
        try { _lifetime.Cancel(); }
        catch (Exception exception)
        {
            _log.SafeError(nameof(SearchBrowserHost), "cancel-browser-lifetime", exception);
        }
        activeOperation?.CancelBeforeStart();
        _ = CompleteStopAsync(activeOperation, completion);
        return completion.Task;
    }

    private async Task CompleteStopAsync(
        SearchBrowserUiOperation? activeOperation,
        TaskCompletionSource completion)
    {
        try
        {
            await StopCoreAsync(activeOperation).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception) { completion.TrySetException(exception); }
    }

    private async Task StopCoreAsync(SearchBrowserUiOperation? activeOperation)
    {
        var uiCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_view is null && _webView is null)
        {
            uiCleanup.TrySetResult();
        }
        else if (!_dispatcher.TryPost(() =>
            {
                try
                {
                    var view = _view;
                    var cleanup = _cleanupView;
                    try
                    {
                        if (view is not null && !view.IsClosed) view.Window.Close();
                    }
                    finally { cleanup?.Invoke(); }
                }
                catch (Exception exception)
                {
                    _log.SafeError(nameof(SearchBrowserHost), "close-browser-ui", exception);
                }
                finally { uiCleanup.TrySetResult(); }
            }))
        {
            uiCleanup.TrySetResult();
        }

        await uiCleanup.Task.ConfigureAwait(false);
        if (activeOperation is not null) await activeOperation.UiFinished.ConfigureAwait(false);
        await _dispatcher.StopAsync().ConfigureAwait(false);
    }
}
