using Windows.Globalization;
using Windows.Media.Ocr;

namespace CircleToSearch.TextRecognition;

public sealed record OcrLanguageOption(string Tag, string DisplayName);

public sealed class OcrLanguageCatalog
{
    private readonly IReadOnlyList<OcrLanguageOption> _languages;

    public OcrLanguageCatalog()
    {
        try
        {
            _languages = OcrEngine.AvailableRecognizerLanguages
                .Select(language => new OcrLanguageOption(language.LanguageTag, language.DisplayName))
                .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is TypeLoadException or PlatformNotSupportedException)
        {
            _languages = [];
        }
    }

    internal OcrLanguageCatalog(IReadOnlyList<OcrLanguageOption> languages) =>
        _languages = (languages ?? throw new ArgumentNullException(nameof(languages))).ToArray();

    public IReadOnlyList<OcrLanguageOption> AvailableLanguages => _languages;

    public string? Validate(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return null;
        return _languages.FirstOrDefault(language =>
            string.Equals(language.Tag, languageTag, StringComparison.OrdinalIgnoreCase))?.Tag;
    }

    public OcrLanguageOption? Resolve(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return null;
        var exact = _languages.FirstOrDefault(language =>
            string.Equals(language.Tag, languageTag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var requested = Parts(languageTag);
        return _languages
            .Where(option => Parts(option.Tag) == requested)
            .OrderBy(option => option.Tag, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static (string Language, string Script) Parts(string tag)
    {
        var parts = tag.Split('-');
        var script = parts.FirstOrDefault(part => part.Length == 4 && part.All(char.IsLetter)) ?? string.Empty;
        if (parts[0].Equals("zh", StringComparison.OrdinalIgnoreCase) && script.Length == 0)
        {
            var region = parts.Skip(1).FirstOrDefault(part => part.Length == 2 && part.All(char.IsLetter));
            script = region?.ToUpperInvariant() switch
            {
                "CN" or "SG" => "Hans",
                "TW" or "HK" or "MO" => "Hant",
                _ => string.Empty,
            };
        }
        return (parts[0].ToLowerInvariant(), script.ToLowerInvariant());
    }

    internal static Language? TryCreateLanguage(string tag)
    {
        try { return new Language(tag); }
        catch (ArgumentException) { return null; }
    }
}
