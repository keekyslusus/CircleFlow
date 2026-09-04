namespace CircleToSearch.TextRecognition;

public sealed class AutomaticOcrLanguageResolver
{
    public IReadOnlyList<string> Resolve(IReadOnlyList<OcrLanguageOption> availableLanguages)
    {
        ArgumentNullException.ThrowIfNull(availableLanguages);
        var tags = availableLanguages
            .Select(language => language.Tag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tag => tag, StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Array.AsReadOnly(tags);
    }
}
