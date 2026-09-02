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

    internal static Language? TryCreateLanguage(string tag)
    {
        try { return new Language(tag); }
        catch (ArgumentException) { return null; }
    }
}
