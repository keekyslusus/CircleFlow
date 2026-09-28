using CircleToSearch.Ui;

namespace CircleToSearch.Updates;

internal sealed record AvailableUpdate(string Version, Func<CancellationToken, Task> DownloadAsync, Action ApplyAfterExit);

internal sealed class UpdateService(
    Func<CancellationToken, Task<AvailableUpdate?>> findAsync,
    IPluginNotifier notifier,
    Func<Task> requestExitAsync,
    UiStrings strings,
    PluginLog log,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
{
    // Sign-in launches start before the network is up; an immediate check would only fail.
    internal static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    private readonly CancellationTokenSource _stop = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync = delayAsync ?? Task.Delay;
    private Task _loop = Task.CompletedTask;
    private int _installing;

    public void Start() => _loop = Task.Run(() => RunAsync(_stop.Token));

    public void Stop() => _stop.Cancel();

    public async Task StopAsync()
    {
        _stop.Cancel();
        try { await _loop; }
        catch (OperationCanceledException) { }
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        var wait = FirstCheckDelay;
        while (true)
        {
            await _delayAsync(wait, cancellation);
            wait = await CheckAsync(cancellation) ? CheckInterval : RetryInterval;
        }
    }

    internal async Task<bool> CheckAsync(CancellationToken cancellation)
    {
        if (Volatile.Read(ref _installing) != 0) return true;
        try
        {
            var update = await findAsync(cancellation);
            if (update is null) return true;
            log.Info(nameof(UpdateService), $"version {update.Version} is available");
            notifier.ShowMessageWithButton(strings.UpdateAvailableTitle, strings.UpdateAvailable(update.Version),
                strings.UpdateInstall, () => _ = InstallAsync(update));
            return true;
        }
        catch (Exception exception) when (!cancellation.IsCancellationRequested)
        {
            log.SafeError(nameof(UpdateService), "check", exception);
            return false;
        }
    }

    internal async Task InstallAsync(AvailableUpdate update)
    {
        if (Interlocked.Exchange(ref _installing, 1) != 0) return;
        try
        {
            notifier.ShowMessage(strings.UpdateDownloadingTitle, strings.UpdateDownloading(update.Version));
            await update.DownloadAsync(_stop.Token);
            update.ApplyAfterExit();
            log.Info(nameof(UpdateService), $"exiting to install version {update.Version}");
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _installing, 0);
            if (_stop.IsCancellationRequested) return;
            log.SafeError(nameof(UpdateService), "install", exception);
            notifier.ShowError(strings.UpdateFailedTitle, strings.UpdateFailed);
            return;
        }
        try { await requestExitAsync(); }
        catch (Exception exception) { log.SafeError(nameof(UpdateService), "exit-for-update", exception); }
    }
}
