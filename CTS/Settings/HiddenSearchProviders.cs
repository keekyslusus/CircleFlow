namespace CircleToSearch.Settings;

// Stored as one comma-separated string so AppSettings keeps value equality.
internal static class HiddenSearchProviders
{
    private const char Separator = ',';

    public static IReadOnlyList<string> Parse(string? providerIds) =>
        (providerIds ?? string.Empty).Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Format(IEnumerable<string> providerIds) => string.Join(Separator, providerIds);
}
