using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchBrowserUiOperationTests
{
    [Fact]
    public async Task Public_result_completes_only_after_ui_cleanup_finishes()
    {
        var operation = new SearchBrowserUiOperation();
        Assert.True(operation.TryStart());
        operation.SetOutcome(new SearchBrowserShowResult(SearchBrowserShowStatus.Shown));

        Assert.False(operation.UiFinished.IsCompleted);
        Assert.False(operation.Result.IsCompleted);

        operation.Finish();

        await operation.UiFinished;
        Assert.Equal(SearchBrowserShowStatus.Shown, (await operation.Result).Status);
    }

    [Fact]
    public async Task Queued_operation_can_be_canceled_before_dispatcher_shutdown()
    {
        var operation = new SearchBrowserUiOperation();

        Assert.True(operation.CancelBeforeStart());

        await operation.UiFinished;
        Assert.Equal(SearchBrowserShowStatus.Canceled, (await operation.Result).Status);
        Assert.False(operation.TryStart());
    }

    [Fact]
    public async Task Started_operation_must_finish_its_ui_path_after_cancellation()
    {
        var operation = new SearchBrowserUiOperation();
        Assert.True(operation.TryStart());

        Assert.False(operation.CancelBeforeStart());
        Assert.False(operation.Result.IsCompleted);
        Assert.False(operation.UiFinished.IsCompleted);

        operation.SetOutcome(new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled));
        operation.Finish();

        await operation.UiFinished;
        Assert.Equal(SearchBrowserShowStatus.Canceled, (await operation.Result).Status);
    }
}
