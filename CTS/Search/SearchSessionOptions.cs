using System.Globalization;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;
using CircleToSearch.Interop;

namespace CircleToSearch.Search;

public sealed record SearchSessionOptions(
    int HideDelayMilliseconds = 60,
    int PaddingPx = 8,
    int LassoMinDiagonalPx = 12,
    int MaxLongSidePx = 1600,
    string? OcrLanguageTag = null,
    string TranslationTargetLanguageTag = "en",
    KeyboardLanguageSnapshot InputLanguage = default,
    string TextSearchEngineId = TextSearchEngines.MatchImageSearch)
{
    internal static SearchSessionOptions From(AppSettings settings, OcrLanguageCatalog languages, CultureInfo culture,
        KeyboardLanguageSnapshot inputLanguage = default) =>
        new(settings.HideDelayMilliseconds, settings.PaddingPx, settings.LassoMinDiagonalPx, settings.MaxLongSidePx,
            languages.Resolve(inputLanguage.Tag)?.Tag,
            string.IsNullOrWhiteSpace(settings.TranslationTargetLanguageTag)
                ? string.IsNullOrWhiteSpace(culture.Name) ? "en" : culture.Name
                : settings.TranslationTargetLanguageTag,
            inputLanguage,
            settings.TextSearchEngineId);
}
