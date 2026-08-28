namespace CircleToSearch.Ui;

using System.Windows.Media;

internal static class PluginPalette
{
    public static PluginThemePalette For(bool lightTheme) => lightTheme ? Light : Dark;

    public static Color SystemAccentFallback { get; } = Color.FromRgb(0x00, 0x78, 0xD4);
    public static Color Transparent { get; } = Colors.Transparent;
    public static Color OpaqueBlack { get; } = Colors.Black;
    public static Color SelectionDim { get; } = Color.FromArgb(0x59, 0x00, 0x00, 0x00);
    public static Color SelectionSheen { get; } = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
    public static Color SelectionHalo { get; } = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
    public static Color SelectionFrameFill { get; } = Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);
    public static Color EntranceParticle { get; } = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);

    public static IReadOnlyList<Color> GoogleLensLoadingDots { get; } = Array.AsReadOnly(
        new[]
        {
            Color.FromRgb(0x42, 0x85, 0xF4),
            Color.FromRgb(0xA1, 0x42, 0xF4),
            Color.FromRgb(0x0B, 0x57, 0xD0),
        });

    public static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(255 * alpha), color.R, color.G, color.B);

    private static PluginThemePalette Dark { get; } = new(
        WindowSurface: Color.FromRgb(0x20, 0x21, 0x24),
        PrimaryText: Color.FromRgb(0xE8, 0xEA, 0xED),
        SelectionChip: new SelectionChipPalette(
            Surface: Color.FromArgb(0xE6, 0x20, 0x21, 0x24),
            Label: Color.FromRgb(0xF1, 0xF3, 0xF4),
            Hint: Color.FromRgb(0xC4, 0xC7, 0xC5),
            Icon: Color.FromRgb(0xF1, 0xF3, 0xF4),
            KeycapBackground: Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            KeycapBorder: Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF),
            KeycapText: Color.FromRgb(0xE8, 0xEA, 0xED),
            Divider: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            NeutralOutline: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            ShadowDepth: 6,
            ShadowOpacity: 0.35),
        MusicButton: new MusicButtonPalette(
            Surface: Color.FromArgb(0xE6, 0x20, 0x21, 0x24),
            Foreground: Color.FromRgb(0xF1, 0xF3, 0xF4),
            Border: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)));

    private static PluginThemePalette Light { get; } = new(
        WindowSurface: Color.FromRgb(0xF7, 0xF9, 0xFC),
        PrimaryText: Color.FromRgb(0x30, 0x34, 0x3A),
        SelectionChip: new SelectionChipPalette(
            Surface: Color.FromArgb(0xF0, 0xFC, 0xFC, 0xFD),
            Label: Color.FromRgb(0x1F, 0x20, 0x23),
            Hint: Color.FromRgb(0x5F, 0x63, 0x68),
            Icon: Color.FromRgb(0x3C, 0x40, 0x43),
            KeycapBackground: Color.FromArgb(0x0D, 0x20, 0x21, 0x24),
            KeycapBorder: Color.FromArgb(0x24, 0x20, 0x21, 0x24),
            KeycapText: Color.FromRgb(0x3C, 0x40, 0x43),
            Divider: Color.FromArgb(0x29, 0x20, 0x21, 0x24),
            NeutralOutline: Color.FromArgb(0x2E, 0x20, 0x21, 0x24),
            ShadowDepth: 8,
            ShadowOpacity: 0.3),
        MusicButton: new MusicButtonPalette(
            Surface: Color.FromArgb(0xF0, 0xFC, 0xFC, 0xFD),
            Foreground: Color.FromRgb(0x3C, 0x40, 0x43),
            Border: Color.FromArgb(0x2E, 0x20, 0x21, 0x24)));
}

internal sealed record PluginThemePalette(
    Color WindowSurface,
    Color PrimaryText,
    SelectionChipPalette SelectionChip,
    MusicButtonPalette MusicButton);

internal sealed record SelectionChipPalette(
    Color Surface,
    Color Label,
    Color Hint,
    Color Icon,
    Color KeycapBackground,
    Color KeycapBorder,
    Color KeycapText,
    Color Divider,
    Color NeutralOutline,
    double ShadowDepth,
    double ShadowOpacity);

internal sealed record MusicButtonPalette(Color Surface, Color Foreground, Color Border);
