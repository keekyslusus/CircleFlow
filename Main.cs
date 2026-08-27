using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace CircleToSearch;

public sealed class Main : IAsyncPlugin, ISettingProvider, IDisposable
{
    private PluginRuntime? _runtime;

    public Task InitAsync(PluginInitContext context)
    {
        _runtime = CompositionRoot.Create(context);
        return Task.CompletedTask;
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        var runtime = _runtime;
        if (runtime is null) return Task.FromResult(new List<Result>());
        try
        {
            return Task.FromResult(runtime.QueryTrigger.Build(query.Search ?? string.Empty));
        }
        catch (Exception)
        {
            return Task.FromResult(new List<Result>());
        }
    }

    public Control CreateSettingPanel()
        => _runtime?.CreateSettingPanel() ?? new UserControl();

    public void Dispose()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
