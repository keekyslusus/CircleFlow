using CircleToSearch.Search;
using CircleToSearch.Settings;

namespace CircleToSearch;

public sealed class AppRuntime : IAsyncDisposable
{
    private readonly SearchCoordinator _coordinator;
    private readonly PluginRuntimeLifetime _lifetime;
    private readonly object _gate = new();
    private Task? _stop;

    internal AppRuntime(SearchCoordinator coordinator, PluginRuntimeLifetime lifetime, SettingsService settings,
        ProviderSelectionStore providers)
    {
        _coordinator = coordinator;
        _lifetime = lifetime;
        Settings = settings;
        Providers = providers;
    }

    public Task OpenAsync() => _coordinator.OpenAsync();
    public SettingsService Settings { get; }
    internal ProviderSelectionStore Providers { get; }

    internal void RequestStop() => _coordinator.RequestStop();

    public Task StopAsync()
    {
        RequestStop();
        lock (_gate) return _stop ??= Task.Run(_lifetime.StopAsync);
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
