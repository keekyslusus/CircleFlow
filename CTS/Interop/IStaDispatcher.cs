namespace CircleToSearch.Interop;

internal interface IStaDispatcher : IDisposable
{
    bool TryPost(Action action);

    void Send(Action action);

    Task StopAsync();
}
