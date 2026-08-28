namespace CircleToSearch.Search;

public static class SearchProviderIds
{
    public const string GoogleLens = "google-lens";
    public const string YandexImages = "yandex-images";
}

public sealed record SearchProviderDescriptor(string Id, string DisplayName);

public sealed record VisualSearchProviderRegistration(
    SearchProviderDescriptor Descriptor,
    Func<IVisualSearchProvider> Factory);

public sealed record RoutedVisualSearchOutcome(
    string ProviderId,
    string ProviderDisplayName,
    VisualSearchOutcome Outcome,
    bool UsedFallback);
