using System.Globalization;
using CircleToSearch.Capture;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;
using CircleToSearch.Interop;
using CircleToSearch.Translation;

namespace CircleToSearch.Search;

public sealed record SearchSessionOptions(
    int HideDelayMilliseconds = 60,
    int PaddingPx = 8,
    int LassoMinDiagonalPx = 12,
    int MaxLongSidePx = 1600,
    string? OcrLanguageTag = null,
    string TranslationTargetLanguageTag = "en",
    KeyboardLanguageSnapshot InputLanguage = default,
    string TextSearchEngineId = TextSearchEngines.MatchImageSearch,
    SelectionToolbarAction HiddenToolbarActions = SelectionToolbarAction.None,
    bool ScanQrCodes = false,
    bool Zoom = false)
{
    internal static SearchSessionOptions From(AppSettings settings, OcrLanguageCatalog languages, CultureInfo culture,
        KeyboardLanguageSnapshot inputLanguage = default) =>
        new(settings.HideDelayMilliseconds, settings.PaddingPx, settings.LassoMinDiagonalPx, settings.MaxLongSidePx,
            languages.Resolve(settings.OcrLanguageTag)?.Tag ?? languages.Resolve(inputLanguage.Tag)?.Tag,
            TranslationTargetLanguage.From(culture),
            inputLanguage,
            settings.TextSearchEngineId,
            settings.HiddenToolbarActions,
            settings.ScanQrCodes,
            settings.OverlayZoom);
}
