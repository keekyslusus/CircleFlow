using CircleToSearch.Search;
using CircleToSearch.Settings;

namespace CircleToSearch;

public sealed class AppRuntime : IDisposable
{
    private readonly SearchCoordinator _coordinator;
    private readonly PluginRuntimeStopAdapter _stopAdapter;

    internal AppRuntime(SearchCoordinator coordinator, PluginRuntimeStopAdapter stopAdapter, SettingsService settings)
    {
        _coordinator = coordinator;
        _stopAdapter = stopAdapter;
        Settings = settings;
    }

    public Task OpenAsync() => _coordinator.OpenAsync();
    public SettingsService Settings { get; }

    public Task StopAsync() => _stopAdapter.StopAsync();

    public void Dispose() => _stopAdapter.Dispose();
}
