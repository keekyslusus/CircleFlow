using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchCoordinatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Idle_trigger_runs_the_workflow_and_returns_to_idle(bool hotkey)
    {
        var harness = new Harness();

        if (hotkey) await harness.Coordinator.StartFromHotkeyAsync();
        else await harness.Coordinator.StartFromQueryAsync();

        Assert.Equal(1, harness.Workflow.Calls);
        Assert.Equal(1, harness.Hidden);
        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
    }

    [Fact]
    public async Task Repeated_hotkey_during_selection_cancels_the_active_session()
    {
        var harness = new Harness();
        harness.Workflow.BlockUntilCanceled = true;

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.Selecting));

        await harness.Coordinator.StartFromHotkeyAsync();
        await session;

        Assert.Equal(1, harness.Workflow.Calls);
        Assert.True(harness.Workflow.CancellationObserved);
        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
    }

    [Theory]
    [InlineData(SearchState.RecognizingMusic)]
    [InlineData(SearchState.ShowingMusicResult)]
    public async Task Hotkey_during_cancelable_workflow_state_cancels_the_session(SearchState state)
    {
        var harness = new Harness();
        harness.Workflow.TransitionBeforeBlocking = state;
        harness.Workflow.BlockUntilCanceled = true;

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, state));

        await harness.Coordinator.StartFromHotkeyAsync();
        await session;

        Assert.True(harness.Workflow.CancellationObserved);
        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
    }

    [Fact]
    public async Task Hotkey_during_upload_is_ignored_without_canceling_the_workflow()
    {
        var harness = new Harness();
        harness.Workflow.TransitionBeforeBlocking = SearchState.Uploading;
        harness.Workflow.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.Uploading));

        await harness.Coordinator.StartFromHotkeyAsync();
        Assert.False(harness.Workflow.CancellationObserved);
        harness.Workflow.Completion.SetResult();
        await session;

        Assert.Equal(1, harness.Workflow.Calls);
    }

    [Theory]
    [InlineData(SearchState.Selecting)]
    [InlineData(SearchState.Uploading)]
    [InlineData(SearchState.RecognizingMusic)]
    [InlineData(SearchState.ShowingMusicResult)]
    public async Task Query_during_an_active_session_is_ignored(SearchState state)
    {
        var harness = new Harness();
        harness.Workflow.TransitionBeforeBlocking = state;
        harness.Workflow.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, state));

        await harness.Coordinator.StartFromQueryAsync();
        harness.Workflow.Completion.SetResult();
        await session;

        Assert.Equal(1, harness.Workflow.Calls);
        Assert.False(harness.Workflow.CancellationObserved);
    }

    [Fact]
    public async Task Cancel_while_idle_is_a_noop()
    {
        var harness = new Harness();

        await harness.Coordinator.CancelActiveSession();

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        Assert.Equal(0, harness.Workflow.Calls);
    }

    [Fact]
    public async Task Workflow_exception_surfaces_one_failure_and_returns_to_idle()
    {
        var harness = new Harness();
        harness.Workflow.Exception = new InvalidOperationException("workflow exploded");

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Contains("workflow exploded", Assert.Single(harness.Notifier.Errors));
        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
    }

    [Fact]
    public async Task Cancellation_does_not_surface_a_failure()
    {
        var harness = new Harness();
        harness.Workflow.BlockUntilCanceled = true;

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.Selecting));
        await harness.Coordinator.CancelActiveSession();
        await session;

        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Hide_failure_is_suppressed_and_workflow_still_runs()
    {
        var harness = new Harness(hideMainWindow: () => throw new InvalidOperationException("host unavailable"));

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(1, harness.Workflow.Calls);
        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task A_new_session_can_start_after_the_previous_one_finishes()
    {
        var harness = new Harness();

        await harness.Coordinator.StartFromHotkeyAsync();
        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(2, harness.Workflow.Calls);
    }

    [Fact]
    public async Task Hide_delay_observes_session_cancellation()
    {
        var harness = new Harness(hideDelayMilliseconds: 30_000);

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.Selecting));
        await harness.Coordinator.StartFromHotkeyAsync();
        await session;

        Assert.Equal(0, harness.Workflow.Calls);
        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
    }

    private static bool WaitForState(SearchCoordinator coordinator, SearchState state) =>
        SpinWait.SpinUntil(() => coordinator.State == state, TimeSpan.FromSeconds(5));

    private sealed class Harness
    {
        public Harness(
            Action? hideMainWindow = null,
            int hideDelayMilliseconds = 0)
        {
            var logDirectory = Path.Combine(
                Path.GetTempPath(),
                "CircleToSearch.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logDirectory);
            var settings = new PluginSettings { HideDelayMilliseconds = hideDelayMilliseconds };
            Workflow = new FakeWorkflow();
            Notifier = new FakeNotifier();
            Coordinator = new SearchCoordinator(
                Workflow,
                hideMainWindow ?? (() => Hidden++),
                settings,
                Notifier,
                TestUiStrings.English,
                new PluginLog(logDirectory));
        }

        public SearchCoordinator Coordinator { get; }
        public FakeWorkflow Workflow { get; }
        public FakeNotifier Notifier { get; }
        public int Hidden { get; private set; }
    }

    private sealed class FakeWorkflow : ISearchSessionWorkflow
    {
        public int Calls { get; private set; }
        public bool BlockUntilCanceled { get; set; }
        public bool CancellationObserved { get; private set; }
        public SearchState? TransitionBeforeBlocking { get; set; }
        public TaskCompletionSource? Completion { get; set; }
        public Exception? Exception { get; set; }

        public async Task RunAsync(Action<SearchState> transition, CancellationToken cancellationToken)
        {
            Calls++;
            if (Exception is not null) throw Exception;
            if (TransitionBeforeBlocking is { } state) transition(state);
            if (BlockUntilCanceled)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }
            if (Completion is not null)
            {
                using var registration = cancellationToken.Register(() =>
                {
                    CancellationObserved = true;
                    Completion.TrySetCanceled(cancellationToken);
                });
                await Completion.Task;
            }
        }
    }

    private sealed class FakeNotifier : IPluginNotifier
    {
        public List<string> Errors { get; } = [];
        public void ShowMessage(string title, string message) { }
        public void ShowMessageWithButton(string title, string message, string button, Action action) { }
        public void ShowError(string title, string message) => Errors.Add(message);
    }
}
