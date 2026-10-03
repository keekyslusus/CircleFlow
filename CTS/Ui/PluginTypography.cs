namespace CircleToSearch.Ui;

using System.Windows.Media;

internal static class PluginTypography
{
    // Segoe UI Variable ships with Windows 11 only; Windows 10 falls back to Segoe UI.
    public static FontFamily Font { get; } = new("Segoe UI Variable Text, Segoe UI");

    public const double Caption = 12;
    public const double Body = 14;
    public const double Subtitle = 16;
    public const double Title = 20;
    public const double Display = 28;
}
