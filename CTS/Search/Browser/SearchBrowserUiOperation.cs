namespace CircleToSearch.Search.Browser;

internal sealed class SearchBrowserUiOperation
{
    private readonly TaskCompletionSource<SearchBrowserShowResult> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _uiFinished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SearchBrowserShowResult? _outcome;
    private int _state;

    public Task<SearchBrowserShowResult> Result => _result.Task;

    public Task UiFinished => _uiFinished.Task;

    public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    public void SetOutcome(SearchBrowserShowResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _outcome ??= outcome;
    }

    public void Finish()
    {
        if (Interlocked.Exchange(ref _state, 2) == 2) return;
        var outcome = _outcome ?? new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled);
        _uiFinished.TrySetResult();
        _result.TrySetResult(outcome);
    }

    public bool CancelBeforeStart()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) != 0) return false;
        _uiFinished.TrySetResult();
        _result.TrySetResult(new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled));
        return true;
    }
}
