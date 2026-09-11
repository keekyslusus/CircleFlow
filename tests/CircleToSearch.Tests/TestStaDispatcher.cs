using CircleToSearch.Interop;

namespace CircleToSearch.Tests;

internal sealed class TestStaDispatcher : IStaDispatcher
{
    public bool TryPostResult { get; set; } = true;
    public bool ExecutePostedAction { get; set; }
    public bool ExecuteSentAction { get; set; } = true;
    public int TryPostCalls { get; private set; }
    public int SendCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public int StopCalls { get; private set; }
    public Action? BeforeSendAction { get; set; }

    public bool TryPost(Action action)
    {
        TryPostCalls++;
        if (!TryPostResult) return false;
        if (ExecutePostedAction) action();
        return true;
    }

    public void Send(Action action)
    {
        SendCalls++;
        BeforeSendAction?.Invoke();
        if (ExecuteSentAction) action();
    }

    public void Dispose() => DisposeCalls++;

    public Task StopAsync()
    {
        StopCalls++;
        Dispose();
        return Task.CompletedTask;
    }
}
