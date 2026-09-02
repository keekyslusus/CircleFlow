namespace CircleToSearch.Settings;

using CircleToSearch.Search;

public sealed class PluginSettings
{
    public string SearchProviderId { get; set; } = SearchProviderIds.GoogleLens;

    public string HotkeyGesture { get; set; } = "Ctrl+Alt+Space";

    public int MaxLongSidePx { get; set; } = 1600;

    public int PaddingPx { get; set; } = 8;

    public int HideDelayMilliseconds { get; set; } = 60;

    public int LassoMinDiagonalPx { get; set; } = 12;

    public string OcrLanguageTag { get; set; } = string.Empty;

    public string TranslationTargetLanguageTag { get; set; } = string.Empty;

    public bool TranslationPrivacyConsentAccepted { get; set; }
}
