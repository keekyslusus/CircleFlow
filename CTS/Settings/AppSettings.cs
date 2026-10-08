namespace CircleToSearch.Settings;

using CircleToSearch.Capture;
using CircleToSearch.Search;

public sealed record AppSettings
{
    public string SearchProviderId { get; init; } = SearchProviderIds.GoogleLens;

    public string HiddenSearchProviderIds { get; init; } = SearchProviderIds.YandexImages;

    public string TextSearchEngineId { get; init; } = TextSearchEngines.MatchImageSearch;

    public bool TextSearchInBuiltInBrowser { get; init; }

    public string HotkeyGesture { get; init; } = "Ctrl+Alt+Space";

    public bool IgnoreHotkeyInFullscreen { get; init; } = true;

    public bool UiSounds { get; init; } = true;

    public SelectionToolbarAction HiddenToolbarActions { get; init; }

    public bool ScanQrCodes { get; init; } = true;

    public int BrowserDataCleanupDays { get; init; } = 28;

    public bool SaveMusicHistory { get; init; } = true;

    public int MusicHistoryRetentionDays { get; init; } = 28;

    public int MaxLongSidePx { get; init; } = 1600;

    public int PaddingPx { get; init; } = 8;

    public int HideDelayMilliseconds { get; init; } = 60;

    public int LassoMinDiagonalPx { get; init; } = 12;

    public string OcrLanguageTag { get; init; } = string.Empty;

    public string AppLanguageTag { get; init; } = string.Empty;

    public bool ImageTranslationPrivacyConsentAccepted { get; init; }

    public bool OnboardingCompleted { get; init; }
}
