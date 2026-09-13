using CircleToSearch.Search;

namespace CircleToSearch;

public sealed class AppRuntime : IDisposable
{
    private readonly SearchCoordinator _coordinator;
    private readonly PluginRuntimeStopAdapter _stopAdapter;

    internal AppRuntime(SearchCoordinator coordinator, PluginRuntimeStopAdapter stopAdapter)
    {
        _coordinator = coordinator;
        _stopAdapter = stopAdapter;
    }

    public Task OpenAsync() => _coordinator.StartFromQueryAsync();

    public Task StopAsync() => _stopAdapter.StopAsync();

    public void Dispose() => _stopAdapter.Dispose();
}
