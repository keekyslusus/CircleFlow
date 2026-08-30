using CircleToSearch.Settings;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal sealed class ProviderSelectionStore(
    VisualSearchProviderRouter providerRouter,
    PluginSettings settings,
    Action saveSettings,
    IPluginNotifier notifier,
    UiStrings strings,
    PluginLog log)
{
    public IReadOnlyList<SearchProviderDescriptor> Providers => providerRouter.Providers;

    public SearchProviderDescriptor GetEffectiveSelection() =>
        providerRouter.GetEffectiveDescriptor(settings.SearchProviderId);

    public void Save(string requestedProviderId)
    {
        var selected = providerRouter.GetEffectiveDescriptor(requestedProviderId);
        settings.SearchProviderId = selected.Id;
        try
        {
            saveSettings();
            log.Info(nameof(ProviderSelectionStore), $"visual search provider changed to '{selected.Id}'");
        }
        catch (Exception exception)
        {
            log.Error(nameof(ProviderSelectionStore), "saving the visual search provider failed", exception);
            notifier.ShowError(strings.PluginTitle, strings.SavingFailed(exception.Message));
        }
    }
}
