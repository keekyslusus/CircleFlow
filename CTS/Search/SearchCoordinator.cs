namespace CircleToSearch.Search;

using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.Settings;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

public enum SearchState
{
    Idle,
    Selecting,
    Uploading,
}

public sealed class SearchCoordinator
{
    private readonly VisualSearchProviderRouter _providerRouter;
    private readonly Func<CancellationToken, Task<SelectionOutcome?>> _selection;
    private readonly Func<GdiBitmap, GdiRectangle, byte[]> _crop;
    private readonly Func<string, bool> _openUrl;
    private readonly Action _hideMainWindow;
    private readonly Action<string, string> _showError;
    private readonly PluginSettings _settings;
    private readonly PluginLog _log;
    private readonly SemaphoreSlim _session = new(1, 1);
    private int _state;
    private CancellationTokenSource? _cancellation;

    public SearchCoordinator(
        VisualSearchProviderRouter providerRouter,
        Func<CancellationToken, Task<SelectionOutcome?>> selection,
        Func<GdiBitmap, GdiRectangle, byte[]> crop,
        Func<string, bool> openUrl,
        Action hideMainWindow,
        Action<string, string> showError,
        PluginSettings settings,
        PluginLog log)
    {
        _providerRouter = providerRouter;
        _selection = selection;
        _crop = crop;
        _openUrl = openUrl;
        _hideMainWindow = hideMainWindow;
        _showError = showError;
        _settings = settings;
        _log = log;
    }

    public SearchState State => (SearchState)Volatile.Read(ref _state);

    public Task StartFromHotkeyAsync()
    {
        try
        {
            return State switch
            {
                SearchState.Selecting => CancelActiveSelection(),
                SearchState.Uploading => IgnoreTrigger("upload in progress"),
                _ => RunSessionAsync("hotkey"),
            };
        }
        catch (Exception exception)
        {
            SurfaceFailure("starting the selection failed", exception);
            return Task.CompletedTask;
        }
    }

    public Task StartFromQueryAsync()
    {
        try
        {
            return State == SearchState.Idle
                ? RunSessionAsync("query")
                : IgnoreTrigger("another session is active");
        }
        catch (Exception exception)
        {
            SurfaceFailure("starting the selection failed", exception);
            return Task.CompletedTask;
        }
    }

    public Task CancelActiveSelection()
    {
        try
        {
            _log.Info(nameof(SearchCoordinator), "canceling the active selection");
            Volatile.Read(ref _cancellation)?.Cancel();
        }
        catch (Exception exception)
        {
            _log.Error(nameof(SearchCoordinator), "canceling the selection failed", exception);
        }
        return Task.CompletedTask;
    }

    private async Task RunSessionAsync(string trigger)
    {
        if (!await _session.WaitAsync(0).ConfigureAwait(false))
        {
            _log.Info(nameof(SearchCoordinator), $"trigger '{trigger}' ignored: session already active");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        Volatile.Write(ref _cancellation, cancellation);
        var requestedProviderId = _settings.SearchProviderId;
        try
        {
            SetState(SearchState.Selecting);
            _log.Info(nameof(SearchCoordinator), $"selection started via {trigger}");
            SafeHideMainWindow();
            try
            {
                await Task.Delay(_settings.HideDelayMilliseconds, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var outcome = await _selection(cancellation.Token).ConfigureAwait(false);
            if (outcome is null)
            {
                _log.Info(nameof(SearchCoordinator), "selection canceled");
                return;
            }

            byte[] png;
            try
            {
                png = _crop(outcome.FrozenFrame, outcome.Bounds);
            }
            finally
            {
                outcome.FrozenFrame.Dispose();
            }

            SetState(SearchState.Uploading);
            var selectedProvider = _providerRouter.GetEffectiveDescriptor(requestedProviderId);
            _log.Info(nameof(SearchCoordinator), $"upload started with provider '{selectedProvider.Id}'");
            var routed = await _providerRouter
                .SearchAsync(requestedProviderId, png, cancellation.Token)
                .ConfigureAwait(false);
            var result = routed.Outcome;
            _log.Info(
                nameof(SearchCoordinator),
                $"provider '{routed.ProviderId}' completed with {result.Failure}");
            if (!result.Success)
            {
                var reason = result.Failure switch
                {
                    UploadFailure.UnexpectedStatus => $"The search service answered HTTP {result.StatusCode}.",
                    UploadFailure.BadResponse => "The search service answered with an unexpected response.",
                    UploadFailure.PolicyRejection => "The search service returned an unexpected results location.",
                    UploadFailure.Timeout => "The upload timed out.",
                    UploadFailure.NetworkError => "The upload failed: network error.",
                    UploadFailure.BrowserRuntimeUnavailable =>
                        $"{routed.ProviderDisplayName} requires Microsoft Edge WebView2 Runtime.",
                    UploadFailure.BrowserAutomationFailed =>
                        $"{routed.ProviderDisplayName} opened, but the image could not be attached.",
                    UploadFailure.Canceled => null,
                    _ => "The upload failed.",
                };
                if (reason is null)
                {
                    _log.Info(nameof(SearchCoordinator), "upload canceled");
                    return;
                }
                _log.Warn(
                    nameof(SearchCoordinator),
                    $"provider '{routed.ProviderId}' failed: {result.Failure} status {result.StatusCode}");
                SurfaceError("Circle to Search", reason);
                return;
            }

            if (result.ResultsUrl is { Length: > 0 } url)
            {
                if (!_openUrl(url))
                    SurfaceError("Circle to Search", "The results URL could not be opened in the default browser.");
                else
                    _log.Info(
                        nameof(SearchCoordinator),
                        $"provider '{routed.ProviderId}' results opened in the default browser");
            }
            else
            {
                _log.Info(
                    nameof(SearchCoordinator),
                    $"results delivered by provider '{routed.ProviderId}'");
            }
        }
        catch (OperationCanceledException)
        {
            _log.Info(nameof(SearchCoordinator), "session canceled");
        }
        catch (Exception exception)
        {
            SurfaceFailure("the search failed", exception);
        }
        finally
        {
            SetState(SearchState.Idle);
            Volatile.Write(ref _cancellation, null);
            _session.Release();
        }
    }

    private Task IgnoreTrigger(string reason)
    {
        _log.Info(nameof(SearchCoordinator), $"hotkey ignored: {reason}");
        return Task.CompletedTask;
    }

    private void SafeHideMainWindow()
    {
        try
        {
            _hideMainWindow();
        }
        catch (Exception exception)
        {
            _log.Warn(nameof(SearchCoordinator), $"hiding the Flow window failed: {exception.Message}");
        }
    }

    private void SurfaceError(string title, string message)
    {
        try
        {
            _showError(title, message);
        }
        catch (Exception exception)
        {
            _log.Error(nameof(SearchCoordinator), "showing the error message failed", exception);
        }
    }

    private void SurfaceFailure(string message, Exception exception)
    {
        _log.Error(nameof(SearchCoordinator), message, exception);
        SurfaceError("Circle to Search", $"{message}: {exception.Message}");
    }

    private void SetState(SearchState state) => Volatile.Write(ref _state, (int)state);
}
