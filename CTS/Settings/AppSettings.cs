namespace CircleToSearch.Settings;

using CircleToSearch.Search;

public sealed record AppSettings
{
    public string SearchProviderId { get; init; } = SearchProviderIds.GoogleLens;

    public string HotkeyGesture { get; init; } = "Ctrl+Alt+Space";

    public int MaxLongSidePx { get; init; } = 1600;

    public int PaddingPx { get; init; } = 8;

    public int HideDelayMilliseconds { get; init; } = 60;

    public int LassoMinDiagonalPx { get; init; } = 12;

    public string OcrLanguageTag { get; init; } = string.Empty;

    public string TranslationTargetLanguageTag { get; init; } = string.Empty;
    public bool ImageTranslationPrivacyConsentAccepted { get; init; }
}
