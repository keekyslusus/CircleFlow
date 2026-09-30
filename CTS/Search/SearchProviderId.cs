namespace CircleToSearch.Search;

public static class SearchProviderIds
{
    public const string GoogleLens = "google-lens";
    public const string YandexImages = "yandex-images";
    public const string TraceMoe = "trace-moe";
    public const string Pinterest = "pinterest";
}

public sealed record SearchProviderDescriptor
{
    private readonly Func<string> _displayName;

    public SearchProviderDescriptor(string id, string displayName) : this(id, () => displayName) { }

    // Built-in providers read their name from the current app language, which can change while the app runs.
    public SearchProviderDescriptor(string id, Func<string> displayName)
    {
        Id = id;
        _displayName = displayName;
    }

    public string Id { get; }

    public string DisplayName => _displayName();

    public bool Equals(SearchProviderDescriptor? other) =>
        other is not null && Id == other.Id && DisplayName == other.DisplayName;

    public override int GetHashCode() => HashCode.Combine(Id, DisplayName);
}

public sealed record VisualSearchProviderRegistration(
    SearchProviderDescriptor Descriptor,
    Func<IVisualSearchProvider> Factory);

public sealed record RoutedVisualSearchPreparation(
    string ProviderId,
    string ProviderDisplayName,
    VisualSearchPreparationOutcome Outcome,
    bool UsedFallback);
