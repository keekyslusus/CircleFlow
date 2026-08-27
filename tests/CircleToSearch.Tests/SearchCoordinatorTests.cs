using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using GdiBitmap = System.Drawing.Bitmap;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchCoordinatorTests
{
    [Fact]
    public async Task Full_flow_selects_uploads_and_opens_results()
    {
        var provider = new FakeProvider();
        var opened = new List<string>();
        var hidden = 0;
        var coordinator = NewCoordinator(
            provider,
            ImmediateSelection(),
            openUrl: url => { opened.Add(url); return true; },
            onHide: () => hidden++);

        await coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(["https://lens.google.com/result"], opened);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, hidden);
    }

    [Fact]
    public async Task Repeated_hotkey_during_selection_cancels()
    {
        var provider = new FakeProvider();
        var (selection, gate) = BlockingSelection();
        var coordinator = NewCoordinator(provider, selection);

        var session = coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(coordinator, SearchState.Selecting));

        await coordinator.StartFromHotkeyAsync();
        gate.SetResult(null);
        await session;

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Hotkey_during_upload_is_ignored()
    {
        var provider = new FakeProvider
        {
            Gate = new TaskCompletionSource<VisualSearchOutcome>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var coordinator = NewCoordinator(provider, ImmediateSelection());

        var session = coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(coordinator, SearchState.Uploading));

        await coordinator.StartFromHotkeyAsync();
        provider.Gate!.SetResult(VisualSearchOutcome.Ok("https://lens.google.com/late"));
        await session;

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Canceled_selection_returns_to_idle_without_upload()
    {
        var provider = new FakeProvider();
        var coordinator = NewCoordinator(provider, _ => Task.FromResult<SelectionOutcome?>(null));

        await coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Upload_failure_surfaces_an_error_and_does_not_throw()
    {
        var provider = new FakeProvider { Outcome = VisualSearchOutcome.Fail(UploadFailure.Timeout) };
        var errors = new List<string>();
        var coordinator = NewCoordinator(provider, ImmediateSelection(), errors: errors);

        await coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Single(errors);
        Assert.Contains("timed out", errors[0]);
    }

    [Fact]
    public async Task Browser_failure_surfaces_an_error()
    {
        var provider = new FakeProvider();
        var errors = new List<string>();
        var coordinator = NewCoordinator(provider, ImmediateSelection(), openUrl: _ => false, errors: errors);

        await coordinator.StartFromQueryAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Single(errors);
    }

    [Fact]
    public async Task Selection_failure_is_contained()
    {
        var provider = new FakeProvider();
        var errors = new List<string>();
        var coordinator = NewCoordinator(
            provider,
            _ => throw new InvalidOperationException("capture exploded"),
            errors: errors);

        await coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(0, provider.Calls);
        Assert.Single(errors);
    }

    [Fact]
    public async Task Cancel_while_idle_is_a_noop()
    {
        var coordinator = NewCoordinator(new FakeProvider(), ImmediateSelection());

        await coordinator.CancelActiveSelection();

        Assert.Equal(SearchState.Idle, coordinator.State);
    }

    private static SearchCoordinator NewCoordinator(
        FakeProvider provider,
        Func<CancellationToken, Task<SelectionOutcome?>> selection,
        Func<string, bool>? openUrl = null,
        Action? onHide = null,
        List<string>? errors = null)
    {
        errors ??= [];
        return new SearchCoordinator(
            provider,
            selection,
            (frame, bounds) => [1, 2, 3],
            openUrl ?? (_ => true),
            onHide ?? (() => { }),
            (_, message) => errors.Add(message),
            new PluginSettings(),
            SilentLog());
    }

    private static Func<CancellationToken, Task<SelectionOutcome?>> ImmediateSelection()
        => _ => Task.FromResult<SelectionOutcome?>(new SelectionOutcome(new Rectangle(0, 0, 10, 10), new GdiBitmap(2, 2)));

    private static (Func<CancellationToken, Task<SelectionOutcome?>> Selection, TaskCompletionSource<SelectionOutcome?> Gate)
        BlockingSelection()
    {
        var gate = new TaskCompletionSource<SelectionOutcome?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<SelectionOutcome?>> selection = cancel =>
        {
            cancel.Register(() => gate.TrySetResult(null));
            return gate.Task;
        };
        return (selection, gate);
    }

    private static bool WaitForState(SearchCoordinator coordinator, SearchState state)
        => SpinWait.SpinUntil(() => coordinator.State == state, TimeSpan.FromSeconds(5));

    private static PluginLog SilentLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests");
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class FakeProvider : IVisualSearchProvider
    {
        public int Calls;

        public VisualSearchOutcome Outcome { get; set; } = VisualSearchOutcome.Ok("https://lens.google.com/result");

        public TaskCompletionSource<VisualSearchOutcome>? Gate { get; set; }

        public Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
        {
            Interlocked.Increment(ref Calls);
            return Gate is null ? Task.FromResult(Outcome) : Gate.Task;
        }
    }
}
