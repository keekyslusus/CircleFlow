using System.Globalization;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;

namespace CircleToSearch.Search;

public sealed record SearchSessionOptions(
    int HideDelayMilliseconds = 60,
    int PaddingPx = 8,
    int LassoMinDiagonalPx = 12,
    int MaxLongSidePx = 1600,
    string? OcrLanguageTag = null,
    string TranslationTargetLanguageTag = "en")
{
    internal static SearchSessionOptions From(AppSettings settings, OcrLanguageCatalog languages, CultureInfo culture) =>
        new(settings.HideDelayMilliseconds, settings.PaddingPx, settings.LassoMinDiagonalPx, settings.MaxLongSidePx,
            languages.Validate(settings.OcrLanguageTag),
            string.IsNullOrWhiteSpace(settings.TranslationTargetLanguageTag)
                ? string.IsNullOrWhiteSpace(culture.Name) ? "en" : culture.Name
                : settings.TranslationTargetLanguageTag);
}
