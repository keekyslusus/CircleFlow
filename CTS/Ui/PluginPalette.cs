namespace CircleToSearch.Ui;

using System.Windows.Media;

internal static class PluginPalette
{
    private static Color DarkMusicPrimary { get; } = Color.FromRgb(0xD0, 0xBC, 0xFF);
    private static Color LightMusicPrimary { get; } = Color.FromRgb(0x67, 0x50, 0xA4);
    private static Color DarkDockSurface { get; } = Color.FromArgb(0xE6, 0x20, 0x21, 0x24);
    private static Color DarkDockHoverOverlay { get; } = Color.FromArgb(0x12, 0x00, 0x00, 0x00);
    private static Color LightDockSurface { get; } = Color.FromArgb(0xF0, 0xFC, 0xFC, 0xFD);
    private static Color LightDockHoverOverlay { get; } = Color.FromArgb(0x0D, 0x20, 0x21, 0x24);

    public static PluginThemePalette For(bool lightTheme) => lightTheme ? Light : Dark;

    internal static Color TraceCardHover(bool lightTheme) => Composite(For(lightTheme).MusicOverlay.Surface,
        lightTheme ? Color.FromArgb(15, 0, 0, 0) : Color.FromArgb(20, 255, 255, 255));

    internal static Color TraceTimelineTrack(bool lightTheme) => Composite(For(lightTheme).MusicOverlay.Surface,
        WithAlpha(For(lightTheme).MusicOverlay.Primary, 0.2));

    public static Color SystemAccentFallback { get; } = Color.FromRgb(0x00, 0x78, 0xD4);
    public static Color Transparent { get; } = Colors.Transparent;
    public static Color OpaqueBlack { get; } = Colors.Black;
    public static Color SelectionDim { get; } = Color.FromArgb(0x59, 0x00, 0x00, 0x00);
    public static Color SelectionSheen { get; } = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
    public static Color SelectionHalo { get; } = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
    public static Color SelectionFrameFill { get; } = Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);
    public static Color EntranceParticle { get; } = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);
    public static Color ListeningText { get; } = Color.FromRgb(0xF4, 0xF5, 0xF8);
    public static Color SceneRippleAudio { get; } = Color.FromRgb(0xC5, 0x9B, 0xFF);

    public static IReadOnlyList<Color> SearchBrowserLoadingDots { get; } = Array.AsReadOnly(
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
        SearchBrowserScrollbarThumb: Color.FromArgb(0xA6, 0xE8, 0xEA, 0xED),
        SelectionChip: new SelectionChipPalette(
            Surface: DarkDockSurface,
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
            Surface: DarkDockSurface,
            Foreground: Color.FromRgb(0xF1, 0xF3, 0xF4),
            Border: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            Hover: Composite(DarkDockSurface, DarkDockHoverOverlay)),
        Provider: new ProviderPalette(
            Surface: DarkDockSurface,
            Text: Color.FromRgb(0xF1, 0xF3, 0xF4),
            Hint: Color.FromRgb(0xC4, 0xC7, 0xC5),
            Border: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            Hover: Composite(DarkDockSurface, DarkDockHoverOverlay),
            MenuSurface: Color.FromRgb(0x21, 0x1F, 0x26),
            MenuText: Color.FromRgb(0xE6, 0xE1, 0xE5),
            MenuMutedText: Color.FromRgb(0xCA, 0xC4, 0xD0),
            MenuHover: Color.FromRgb(0x4A, 0x44, 0x58),
            MenuHoverText: Color.FromRgb(0xE8, 0xDE, 0xF8),
            MenuBorder: Color.FromRgb(0x44, 0x47, 0x46),
            MenuShadowOpacity: 0.35,
            Google: Color.FromRgb(0x42, 0x85, 0xF4),
            GoogleRed: Color.FromRgb(0xEA, 0x43, 0x35),
            GoogleYellow: Color.FromRgb(0xFB, 0xBC, 0x05),
            GoogleGreen: Color.FromRgb(0x34, 0xA8, 0x53),
            Yandex: Color.FromRgb(0xFC, 0x3F, 0x1D),
            Trace: Color.FromRgb(0xE5, 0xE8, 0xFF),
            Neutral: Color.FromRgb(0xBD, 0xC1, 0xC6)),
        MusicOverlay: new MusicOverlayPalette(
            Surface: Color.FromRgb(0x21, 0x1F, 0x26),
            Text: Color.FromRgb(0xE6, 0xE1, 0xE5),
            MutedText: Color.FromRgb(0xCA, 0xC4, 0xD0),
            Border: Color.FromRgb(0x44, 0x47, 0x46),
            Primary: DarkMusicPrimary,
            PrimaryContainer: Color.FromRgb(0x4F, 0x37, 0x8B),
            OnPrimaryContainer: Color.FromRgb(0xEA, 0xDD, 0xFF),
            SecondaryContainer: Color.FromRgb(0x4A, 0x44, 0x58),
            OnSecondaryContainer: Color.FromRgb(0xE8, 0xDE, 0xF8),
            ShadowOpacity: 0.35),
        Toast: new ToastPalette(
            Surface: Color.FromRgb(0x21, 0x1F, 0x26),
            Text: Color.FromRgb(0xE6, 0xE1, 0xE5),
            NeutralAccent: Color.FromRgb(0x44, 0x47, 0x46),
            ErrorAccent: Color.FromRgb(0xC9, 0x8B, 0x86),
            SuccessAccent: DarkMusicPrimary,
            ShadowOpacity: 0.35),
        TextInteraction: new TextInteractionPalette(
            Hover: Color.FromArgb(0x24, 0xD0, 0xBC, 0xFF),
            Selection: Color.FromArgb(0x55, 0xD0, 0xBC, 0xFF),
            CardSurface: Color.FromRgb(0x21, 0x1F, 0x26),
            CardText: Color.FromRgb(0xE6, 0xE1, 0xE5),
            CardBorder: Color.FromRgb(0x44, 0x47, 0x46),
            ButtonHover: Color.FromRgb(0x4A, 0x44, 0x58)),
        Translation: new TranslationPalette(
            Surface: DarkDockSurface,
            Text: Color.FromRgb(0xF1, 0xF3, 0xF4),
            Border: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            Hover: Composite(DarkDockSurface, DarkDockHoverOverlay)));

    private static PluginThemePalette Light { get; } = new(
        WindowSurface: Color.FromRgb(0xF7, 0xF9, 0xFC),
        PrimaryText: Color.FromRgb(0x30, 0x34, 0x3A),
        SearchBrowserScrollbarThumb: Color.FromArgb(0x8F, 0x30, 0x34, 0x3A),
        SelectionChip: new SelectionChipPalette(
            Surface: LightDockSurface,
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
            Surface: LightDockSurface,
            Foreground: Color.FromRgb(0x3C, 0x40, 0x43),
            Border: Color.FromArgb(0x2E, 0x20, 0x21, 0x24),
            Hover: Composite(LightDockSurface, LightDockHoverOverlay)),
        Provider: new ProviderPalette(
            Surface: LightDockSurface,
            Text: Color.FromRgb(0x3C, 0x40, 0x43),
            Hint: Color.FromRgb(0x5F, 0x63, 0x68),
            Border: Color.FromArgb(0x2E, 0x20, 0x21, 0x24),
            Hover: Composite(LightDockSurface, LightDockHoverOverlay),
            MenuSurface: Color.FromRgb(0xF3, 0xF3, 0xFA),
            MenuText: Color.FromRgb(0x1C, 0x1B, 0x1F),
            MenuMutedText: Color.FromRgb(0x49, 0x45, 0x4F),
            MenuHover: Color.FromRgb(0xE8, 0xDE, 0xF8),
            MenuHoverText: Color.FromRgb(0x1D, 0x19, 0x2B),
            MenuBorder: Color.FromRgb(0xC4, 0xC7, 0xC5),
            MenuShadowOpacity: 0.12,
            Google: Color.FromRgb(0x42, 0x85, 0xF4),
            GoogleRed: Color.FromRgb(0xEA, 0x43, 0x35),
            GoogleYellow: Color.FromRgb(0xFB, 0xBC, 0x05),
            GoogleGreen: Color.FromRgb(0x34, 0xA8, 0x53),
            Yandex: Color.FromRgb(0xFC, 0x3F, 0x1D),
            Trace: Color.FromRgb(0x45, 0x4A, 0x75),
            Neutral: Color.FromRgb(0x5F, 0x63, 0x68)),
        MusicOverlay: new MusicOverlayPalette(
            Surface: Color.FromRgb(0xF3, 0xF3, 0xFA),
            Text: Color.FromRgb(0x1C, 0x1B, 0x1F),
            MutedText: Color.FromRgb(0x49, 0x45, 0x4F),
            Border: Color.FromRgb(0xC4, 0xC7, 0xC5),
            Primary: LightMusicPrimary,
            PrimaryContainer: Color.FromRgb(0xEA, 0xDD, 0xFF),
            OnPrimaryContainer: Color.FromRgb(0x21, 0x00, 0x5D),
            SecondaryContainer: Color.FromRgb(0xE8, 0xDE, 0xF8),
            OnSecondaryContainer: Color.FromRgb(0x1D, 0x19, 0x2B),
            ShadowOpacity: 0.12),
        Toast: new ToastPalette(
            Surface: Color.FromRgb(0xF3, 0xF3, 0xFA),
            Text: Color.FromRgb(0x1C, 0x1B, 0x1F),
            NeutralAccent: Color.FromRgb(0xC4, 0xC7, 0xC5),
            ErrorAccent: Color.FromRgb(0xB6, 0x5F, 0x58),
            SuccessAccent: LightMusicPrimary,
            ShadowOpacity: 0.12),
        TextInteraction: new TextInteractionPalette(
            Hover: Color.FromArgb(0x24, 0x67, 0x50, 0xA4),
            Selection: Color.FromArgb(0x55, 0x67, 0x50, 0xA4),
            CardSurface: Color.FromRgb(0xF3, 0xF3, 0xFA),
            CardText: Color.FromRgb(0x1C, 0x1B, 0x1F),
            CardBorder: Color.FromRgb(0xC4, 0xC7, 0xC5),
            ButtonHover: Color.FromRgb(0xE8, 0xDE, 0xF8)),
        Translation: new TranslationPalette(
            Surface: LightDockSurface,
            Text: Color.FromRgb(0x3C, 0x40, 0x43),
            Border: Color.FromArgb(0x2E, 0x20, 0x21, 0x24),
            Hover: Composite(LightDockSurface, LightDockHoverOverlay)));

    internal static Color Composite(Color background, Color foreground)
    {
        var foregroundAlpha = foreground.A / 255d;
        var backgroundAlpha = background.A / 255d;
        var alpha = foregroundAlpha + backgroundAlpha * (1 - foregroundAlpha);
        if (alpha == 0) return Transparent;
        byte Blend(byte backgroundChannel, byte foregroundChannel) => (byte)Math.Round(
            (foregroundChannel * foregroundAlpha +
             backgroundChannel * backgroundAlpha * (1 - foregroundAlpha)) / alpha);
        return Color.FromArgb(
            (byte)Math.Round(alpha * 255),
            Blend(background.R, foreground.R),
            Blend(background.G, foreground.G),
            Blend(background.B, foreground.B));
    }
}

internal sealed record PluginThemePalette(
    Color WindowSurface,
    Color PrimaryText,
    Color SearchBrowserScrollbarThumb,
    SelectionChipPalette SelectionChip,
    MusicButtonPalette MusicButton,
    ProviderPalette Provider,
    MusicOverlayPalette MusicOverlay,
    ToastPalette Toast,
    TextInteractionPalette TextInteraction,
    TranslationPalette Translation);

internal sealed record TextInteractionPalette(
    Color Hover,
    Color Selection,
    Color CardSurface,
    Color CardText,
    Color CardBorder,
    Color ButtonHover);

internal sealed record TranslationPalette(
    Color Surface,
    Color Text,
    Color Border,
    Color Hover);

internal sealed record ToastPalette(
    Color Surface,
    Color Text,
    Color NeutralAccent,
    Color ErrorAccent,
    Color SuccessAccent,
    double ShadowOpacity);

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

internal sealed record MusicButtonPalette(Color Surface, Color Foreground, Color Border, Color Hover);

internal sealed record ProviderPalette(
    Color Surface,
    Color Text,
    Color Hint,
    Color Border,
    Color Hover,
    Color MenuSurface,
    Color MenuText,
    Color MenuMutedText,
    Color MenuHover,
    Color MenuHoverText,
    Color MenuBorder,
    double MenuShadowOpacity,
    Color Google,
    Color GoogleRed,
    Color GoogleYellow,
    Color GoogleGreen,
    Color Yandex,
    Color Trace,
    Color Neutral);

internal sealed record MusicOverlayPalette(
    Color Surface,
    Color Text,
    Color MutedText,
    Color Border,
    Color Primary,
    Color PrimaryContainer,
    Color OnPrimaryContainer,
    Color SecondaryContainer,
    Color OnSecondaryContainer,
    double ShadowOpacity);
