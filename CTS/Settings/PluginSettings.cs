namespace CircleToSearch.Settings;

public sealed class PluginSettings
{
    public string HotkeyGesture { get; set; } = "Ctrl+Alt+Space";

    public int MaxLongSidePx { get; set; } = 1600;

    public int PaddingPx { get; set; } = 8;

    public int HideDelayMilliseconds { get; set; } = 60;

    public int LassoMinDiagonalPx { get; set; } = 12;
}
