using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CircleToSearch.Search.Browser;

internal sealed class WebView2VisualSearchBrowserSession(
    WebView2 webView,
    CoreWebView2Environment environment,
    Func<bool> isCurrent) : IVisualSearchBrowserSession
{
    private readonly WebView2 _webView = webView ?? throw new ArgumentNullException(nameof(webView));
    private readonly CoreWebView2Environment _environment =
        environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly Func<bool> _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));

    public Uri? CurrentUri => _isCurrent() ? _webView.Source : null;

    public Task<BrowserNavigationResult> NavigateAsync(
        Uri target,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        RequireTarget(target);
        return NavigateCoreAsync(
            () => _webView.CoreWebView2.Navigate(target.AbsoluteUri),
            timeout,
            cancel);
    }

    public Task<BrowserNavigationResult> NavigatePostAsync(
        Uri target,
        Stream body,
        string headers,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        RequireTarget(target);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(headers);
        return NavigateCoreAsync(
            () =>
            {
                var request = _environment.CreateWebResourceRequest(
                    target.AbsoluteUri,
                    "POST",
                    body,
                    headers);
                _webView.CoreWebView2.NavigateWithWebResourceRequest(request);
            },
            timeout,
            cancel);
    }

    public Task<BrowserNavigationResult> WaitForNavigationAsync(
        TimeSpan timeout,
        CancellationToken cancel)
        => AwaitNavigationAsync(startNavigation: null, timeout, cancel);

    public async Task<string> ExecuteScriptAsync(string script, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(script);
        EnsureCurrent();
        var core = _webView.CoreWebView2;
        try
        {
            return await core.ExecuteScriptAsync(script)
                .WaitAsync(cancel)
                .ConfigureAwait(true);
        }
        catch (Exception) when (cancel.IsCancellationRequested || !_isCurrent())
        {
            throw new OperationCanceledException(cancel);
        }
    }

    public async Task<string?> PostWebMessageAndWaitAsync(
        string message,
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(predicate);
        EnsureCurrent();

        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!_isCurrent()) return;
            try
            {
                var value = args.TryGetWebMessageAsString();
                if (predicate(value)) completion.TrySetResult(value);
            }
            catch (ArgumentException)
            {
            }
        }

        var core = _webView.CoreWebView2;
        core.WebMessageReceived += OnMessage;
        try
        {
            core.PostWebMessageAsString(message);
            return await completion.Task.WaitAsync(timeout, cancel).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception) when (cancel.IsCancellationRequested || !_isCurrent())
        {
            throw new OperationCanceledException(cancel);
        }
        finally
        {
            TryCleanup(() => core.WebMessageReceived -= OnMessage);
        }
    }

    private Task<BrowserNavigationResult> NavigateCoreAsync(
        Action startNavigation,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        EnsureCurrent();
        return AwaitNavigationAsync(startNavigation, timeout, cancel);
    }

    private async Task<BrowserNavigationResult> AwaitNavigationAsync(
        Action? startNavigation,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        EnsureCurrent();
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ulong navigationId = 0;
        var acceptingNavigation = startNavigation is null;

        void OnStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (!_isCurrent() || !acceptingNavigation || navigationId != 0) return;
            navigationId = args.NavigationId;
        }

        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!_isCurrent() || navigationId == 0 || args.NavigationId != navigationId) return;
            completion.TrySetResult(args);
        }

        var core = _webView.CoreWebView2;
        core.NavigationStarting += OnStarting;
        core.NavigationCompleted += OnCompleted;
        try
        {
            if (startNavigation is not null)
            {
                acceptingNavigation = true;
                startNavigation();
            }

            var result = await completion.Task.WaitAsync(timeout, cancel).ConfigureAwait(true);
            return result.IsSuccess
                ? BrowserNavigationResult.Succeeded()
                : BrowserNavigationResult.Failed(result.WebErrorStatus.ToString());
        }
        catch (TimeoutException)
        {
            return BrowserNavigationResult.TimedOut();
        }
        catch (OperationCanceledException)
        {
            return BrowserNavigationResult.Canceled();
        }
        catch (Exception) when (cancel.IsCancellationRequested || !_isCurrent())
        {
            return BrowserNavigationResult.Canceled();
        }
        finally
        {
            TryCleanup(() => core.NavigationStarting -= OnStarting);
            TryCleanup(() => core.NavigationCompleted -= OnCompleted);
        }
    }

    private void EnsureCurrent()
    {
        if (!_isCurrent()) throw new OperationCanceledException();
    }

    private static void RequireTarget(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsAbsoluteUri) throw new ArgumentException("The navigation target must be absolute.", nameof(target));
    }

    internal static void TryCleanup(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch
        {
        }
    }
}
