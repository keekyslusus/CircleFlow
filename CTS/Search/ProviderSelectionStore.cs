using CircleToSearch.Settings;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal sealed class ProviderSelectionStore(
    VisualSearchProviderRouter providerRouter,
    SettingsService settings,
    IPluginNotifier notifier,
    UiStrings strings,
    PluginLog log)
{
    public IReadOnlyList<SearchProviderDescriptor> Providers => providerRouter.Providers;

    public SearchProviderDescriptor GetEffectiveSelection() =>
        providerRouter.GetEffectiveDescriptor(settings.Snapshot.SearchProviderId);

    public bool Save(string requestedProviderId)
    {
        var selected = providerRouter.GetEffectiveDescriptor(requestedProviderId);
        if (settings.SetProvider(selected.Id).Success)
        {
            log.Info(nameof(ProviderSelectionStore), $"visual search provider changed to '{selected.Id}'");
            return true;
        }
        notifier.ShowError(strings.PluginTitle, strings.StorageSaveFailed);
        return false;
    }
}
