using System.Windows.Controls;
using CircleToSearch.Ui;
using Flow.Launcher.Plugin;

namespace CircleToSearch;

public sealed class Main : IAsyncPlugin, ISettingProvider, IPluginI18n, IDisposable
{
    private PluginRuntime? _runtime;
    private UiStrings? _strings;

    public Task InitAsync(PluginInitContext context)
    {
        _strings = CompositionRoot.CreateUiStrings(context);
        _runtime = CompositionRoot.Create(context, _strings);
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
        catch (Exception exception)
        {
            runtime.ReportQueryFailure(exception);
            return Task.FromResult(new List<Result>());
        }
    }

    public Control CreateSettingPanel()
        => _runtime?.CreateSettingPanel() ?? new UserControl();

    public string GetTranslatedPluginTitle() => _strings?.PluginTitle ?? string.Empty;

    public string GetTranslatedPluginDescription() => _strings?.PluginDescription ?? string.Empty;

    public void Dispose()
    {
        _runtime?.Dispose();
        _runtime = null;
        _strings = null;
    }
}
