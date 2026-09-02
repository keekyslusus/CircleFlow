namespace CircleToSearch.Translation;

public static class TranslationLanguageTags
{
    public static string Normalize(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var normalized = tag.Trim().Replace('_', '-').ToLowerInvariant();
        return normalized switch
        {
            "zh-hans" or "zh-cn" or "zh-sg" => "zh-cn",
            "zh-hant" or "zh-tw" or "zh-hk" or "zh-mo" => "zh-tw",
            _ => normalized,
        };
    }

    public static string ToProviderTag(string tag)
        => Normalize(tag);

    public static string ToNeutralProviderTag(string tag)
    {
        var normalized = Normalize(tag);
        if (normalized is "zh-cn" or "zh-tw") return normalized;
        var separator = normalized.IndexOf('-');
        return separator > 0 ? normalized[..separator] : normalized;
    }

    public static bool Equivalent(string first, string second) =>
        string.Equals(ToNeutralProviderTag(first), ToNeutralProviderTag(second), StringComparison.OrdinalIgnoreCase);
}
