namespace CircleToSearch.Settings;

public sealed class PluginSettings
{
    public const string AutoMode = "auto";
    public const string PasteMode = "paste";
    public const string UploadMode = "upload";

    // "paste" is the default: Google grades sessions with signals a background process cannot
    // currently replicate — sessions farmed via WebView2 or headless Chrome (fresh or warmed up
    // with activity) still yield "visual search request is no longer valid" results pages, while
    // a search performed inside the real browser works. "auto" keeps the session-farm fast path
    // wired for the day the farm is trusted again; it must stay opt-in because a poisoned upload
    // succeeds at the HTTP level and cannot trigger the paste fallback.
    public string SearchMode { get; set; } = PasteMode;

    public string HotkeyGesture { get; set; } = "Ctrl+Alt+Space";

    public int MaxLongSidePx { get; set; } = 1600;

    public int PaddingPx { get; set; } = 8;

    public int HideDelayMilliseconds { get; set; } = 60;

    public int LassoMinDiagonalPx { get; set; } = 12;
}
