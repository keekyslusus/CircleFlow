using System.Net.Http;
using CircleToSearch.Ui;
using CircleToSearch.Updates;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class UpdateServiceTests
{
    private static UiStrings Strings => TestUiStrings.English;

    [Fact]
    public async Task Checks_after_startup_then_daily_and_retries_sooner_after_a_failure()
    {
        var notifier = new TestPluginNotifier();
        var results = new Queue<Func<AvailableUpdate?>>(
        [
            () => throw new HttpRequestException("offline"),
            () => null,
            () => Update("1.2.3", _ => Task.CompletedTask, () => { }),
        ]);
        var delays = new List<TimeSpan>();
        var waitedEnough = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Delay(TimeSpan duration, CancellationToken cancellation)
        {
            lock (delays) delays.Add(duration);
            if (delays.Count < 4) return Task.CompletedTask;
            waitedEnough.TrySetResult();
            return Task.Delay(Timeout.Infinite, cancellation);
        }
        var service = new UpdateService(_ => Task.FromResult(results.Dequeue()()), notifier, _ => { }, () => Task.CompletedTask,
            Strings, NewLog(), Delay);

        service.Start();
        await waitedEnough.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync();

        Assert.Equal([UpdateService.FirstCheckDelay, UpdateService.RetryInterval, UpdateService.CheckInterval,
            UpdateService.CheckInterval], delays);
        var offer = Assert.Single(notifier.Buttons);
        Assert.Equal(Strings.UpdateAvailable("1.2.3"), offer.Message);
        Assert.Equal(Strings.UpdateInstall, offer.Button);
        Assert.Empty(notifier.Errors);
    }

    [Fact]
    public async Task Offer_links_the_version_to_its_release_page_when_there_is_one()
    {
        var notifier = new TestPluginNotifier();
        var opened = new List<string>();
        var results = new Queue<AvailableUpdate>(
        [
            new("2.0.0", "https://github.com/owner/app/releases/tag/v2.0.0", _ => Task.CompletedTask, () => { }),
            Update("2.0.1", _ => Task.CompletedTask, () => { }),
        ]);
        var service = new UpdateService(_ => Task.FromResult<AvailableUpdate?>(results.Dequeue()), notifier, opened.Add,
            () => Task.CompletedTask, Strings, NewLog());

        await service.CheckAsync(CancellationToken.None);
        await service.CheckAsync(CancellationToken.None);

        var link = notifier.Buttons[0].Link!;
        Assert.Equal(Strings.UpdateReleaseLink("2.0.0"), link.Text);
        Assert.Contains(link.Text, notifier.Buttons[0].Message);
        link.Open();
        Assert.Equal(["https://github.com/owner/app/releases/tag/v2.0.0"], opened);
        Assert.Null(notifier.Buttons[1].Link);
    }

    [Fact]
    public async Task Update_button_downloads_once_then_applies_after_a_graceful_exit()
    {
        var notifier = new TestPluginNotifier();
        var steps = new List<string>();
        var download = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new UpdateService(
            _ => Task.FromResult<AvailableUpdate?>(Update("2.0.0",
                _ => { steps.Add("download"); return download.Task; },
                () => steps.Add("apply"))),
            notifier, _ => { }, () => { steps.Add("exit"); exited.SetResult(); return Task.CompletedTask; }, Strings, NewLog());

        Assert.Equal(UpdateCheckOutcome.Offered, await service.CheckAsync(CancellationToken.None));
        var offer = Assert.Single(notifier.Buttons);
        offer.Action();
        offer.Action();
        Assert.Equal(UpdateCheckOutcome.Installing, await service.CheckAsync(CancellationToken.None));
        Assert.Single(notifier.Buttons);
        Assert.Equal(["download"], steps);
        Assert.Equal((Strings.UpdateDownloadingTitle, Strings.UpdateDownloading("2.0.0")),
            Assert.Single(notifier.Messages));

        download.SetResult();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["download", "apply", "exit"], steps);
        Assert.Empty(notifier.Errors);
    }

    [Fact]
    public async Task Check_from_settings_reports_up_to_date_and_failure_without_an_offer()
    {
        var notifier = new TestPluginNotifier();
        var results = new Queue<Func<AvailableUpdate?>>([() => null, () => throw new HttpRequestException("offline")]);
        var service = new UpdateService(_ => Task.FromResult(results.Dequeue()()), notifier, _ => { }, () => Task.CompletedTask,
            Strings, NewLog());

        Assert.Equal(UpdateCheckOutcome.UpToDate, await service.CheckNowAsync());
        Assert.Equal(UpdateCheckOutcome.Failed, await service.CheckNowAsync());
        Assert.Empty(notifier.Buttons);
        Assert.Empty(notifier.Errors);
    }

    [Fact]
    public async Task Overlapping_checks_run_one_after_another()
    {
        var first = new TaskCompletionSource<AvailableUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new UpdateService(
            _ => Interlocked.Increment(ref calls) == 1 ? first.Task : Task.FromResult<AvailableUpdate?>(null),
            new TestPluginNotifier(), _ => { }, () => Task.CompletedTask, Strings, NewLog());

        var scheduled = service.CheckAsync(CancellationToken.None);
        var manual = service.CheckNowAsync();
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.False(manual.IsCompleted);

        first.SetResult(null);
        Assert.Equal(UpdateCheckOutcome.UpToDate, await scheduled.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(UpdateCheckOutcome.UpToDate, await manual.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Failed_download_is_reported_and_can_be_retried_without_exiting()
    {
        var notifier = new TestPluginNotifier();
        var attempts = 0;
        var exits = 0;
        var update = Update("2.0.0",
            _ => ++attempts == 1 ? Task.FromException(new HttpRequestException("reset")) : Task.CompletedTask,
            () => { });
        var service = new UpdateService(_ => Task.FromResult<AvailableUpdate?>(update), notifier, _ => { },
            () => { exits++; return Task.CompletedTask; }, Strings, NewLog());

        await service.InstallAsync(update);
        Assert.Equal((Strings.UpdateFailedTitle, Strings.UpdateFailed), Assert.Single(notifier.Errors));
        Assert.Equal(0, exits);

        await service.InstallAsync(update);
        Assert.Equal(2, attempts);
        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task Stopping_cancels_a_download_without_reporting_an_error()
    {
        var notifier = new TestPluginNotifier();
        var exits = 0;
        var update = Update("2.0.0", cancellation => Task.Delay(Timeout.Infinite, cancellation), () => { });
        var service = new UpdateService(_ => Task.FromResult<AvailableUpdate?>(update), notifier, _ => { },
            () => { exits++; return Task.CompletedTask; }, Strings, NewLog());

        var install = service.InstallAsync(update);
        await service.StopAsync();
        await install.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(notifier.Errors);
        Assert.Equal(0, exits);
    }

    private static AvailableUpdate Update(string version, Func<CancellationToken, Task> download, Action apply) =>
        new(version, ReleasePageUrl: null, download, apply);

    private static PluginLog NewLog() =>
        new(Path.Combine(TestOutputPaths.TempDirectory, "updates-" + Guid.NewGuid().ToString("N")));
}
