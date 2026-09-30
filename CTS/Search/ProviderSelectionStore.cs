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

    public bool IsShownInMenu(string providerId) =>
        !HiddenProviderIds().Contains(providerId, StringComparer.OrdinalIgnoreCase);

    public static bool CanHideFromMenu(string providerId) =>
        !string.Equals(providerId, SearchProviderIds.AlwaysInMenu, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<SearchProviderDescriptor> ShownInMenu =>
        [.. Providers.Where(provider => IsShownInMenu(provider.Id))];

    // Hiding switches the selection away, but a hidden id can still come from an edited settings file.
    public IReadOnlyList<SearchProviderDescriptor> MenuProviders(string selectedProviderId) =>
    [
        .. Providers.Where(provider => IsShownInMenu(provider.Id)
            || string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase)),
    ];

    public bool ShowInMenu(string providerId, bool shown)
    {
        var hidden = HiddenProviderIds()
            .Where(id => !string.Equals(id, providerId, StringComparison.OrdinalIgnoreCase));
        if (!shown) hidden = hidden.Append(providerId);
        if (!settings.SetHiddenSearchProviders(hidden).Success) return false;
        // The overlay chip starts on the selected provider. If this save fails, MenuProviders still keeps it.
        if (!shown && string.Equals(GetEffectiveSelection().Id, providerId, StringComparison.OrdinalIgnoreCase)
            && !settings.SetProvider(SearchProviderIds.AlwaysInMenu).Success)
            return false;
        log.Info(nameof(ProviderSelectionStore),
            $"visual search provider '{providerId}' {(shown ? "shown in" : "hidden from")} the provider menu");
        return true;
    }

    private IReadOnlyList<string> HiddenProviderIds() =>
        HiddenSearchProviders.Parse(settings.Snapshot.HiddenSearchProviderIds);
}
