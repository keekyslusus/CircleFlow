namespace CircleToSearch.TextRecognition;

public sealed class AutomaticOcrLanguageResolver
{
    private static readonly string[] PreferredTags = ["ru-RU", "en-US"];

    public IReadOnlyList<string> Resolve(IReadOnlyList<OcrLanguageOption> availableLanguages)
    {
        ArgumentNullException.ThrowIfNull(availableLanguages);
        var tags = availableLanguages
            .Select(language => language.Tag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resolved = new List<string>(PreferredTags.Length);
        foreach (var preferred in PreferredTags)
        {
            var neutral = preferred[..preferred.IndexOf('-')];
            var match = tags.FirstOrDefault(tag => string.Equals(tag, preferred, StringComparison.OrdinalIgnoreCase))
                ?? tags.FirstOrDefault(tag =>
                    tag.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, neutral, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !resolved.Contains(match, StringComparer.OrdinalIgnoreCase)) resolved.Add(match);
        }
        return resolved;
    }
}
