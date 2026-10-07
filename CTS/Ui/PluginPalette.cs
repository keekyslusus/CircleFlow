namespace CircleToSearch.Ui;

using System.Windows.Media;

internal static class PluginPalette
{
    internal static Color AniListBlue { get; } = Color.FromRgb(0x02, 0xA9, 0xFF);
    internal static Color AniListWhite { get; } = Color.FromRgb(0xFE, 0xFE, 0xFE);
    internal static Color PinterestRed { get; } = Color.FromRgb(0xE6, 0x00, 0x23);
    // Pin previews are arbitrary photos, so their overlays stay dark in both themes.
    internal static Color PinterestTileScrim { get; } = Color.FromArgb(0xC7, 0x00, 0x00, 0x00);
    internal static Color PinterestMoreScrim { get; } = Color.FromArgb(0x99, 0x00, 0x00, 0x00);
    internal static Color PinterestTileText { get; } = Color.FromRgb(0xFF, 0xFF, 0xFF);
    internal static Color PinterestTileMutedText { get; } = Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF);
    internal static Color BingTeal { get; } = Color.FromRgb(0x00, 0xCA, 0xCC);
    internal static Color BingBlue { get; } = Color.FromRgb(0x04, 0x8F, 0xCE);
    internal static Color BingSky { get; } = Color.FromRgb(0x00, 0xBB, 0xEC);
    internal static Color BingNavy { get; } = Color.FromRgb(0x27, 0x56, 0xA9);
    internal static Color DuckDuckGoOrange { get; } = Color.FromRgb(0xDE, 0x58, 0x33);
    internal static Color DuckDuckGoFeather { get; } = Color.FromRgb(0xDD, 0xDD, 0xDD);
    internal static Color DuckDuckGoWhite { get; } = Color.FromRgb(0xFF, 0xFF, 0xFF);
    internal static Color DuckDuckGoTie { get; } = Color.FromRgb(0x3C, 0xA8, 0x2B);
    internal static Color DuckDuckGoTieHighlight { get; } = Color.FromRgb(0x4C, 0xBA, 0x3C);
    internal static Color DuckDuckGoBeak { get; } = Color.FromRgb(0xFF, 0xCC, 0x33);
    // The SVG draws the eyes and brows in a group with 80% opacity.
    internal static Color DuckDuckGoEyes { get; } = Color.FromArgb(0xCC, 0x14, 0x30, 0x7E);
    internal static Color KagiYellow { get; } = Color.FromRgb(0xFF, 0xB3, 0x19);
    internal static Color KagiWhite { get; } = Color.FromRgb(0xFF, 0xFF, 0xFF);
    internal static Color KagiInk { get; } = Color.FromRgb(0x18, 0x18, 0x1A);
    internal static Color StartpageViolet { get; } = Color.FromRgb(0x65, 0x63, 0xFF);
    private static ColorRoles DarkRoles { get; } = new(
        Background: Color.FromRgb(0x20, 0x21, 0x24),
        OnBackground: Color.FromRgb(0xE8, 0xEA, 0xED),
        Surface: Color.FromRgb(0x21, 0x1F, 0x26),
        OnSurface: Color.FromRgb(0xE6, 0xE1, 0xE5),
        OnSurfaceVariant: Color.FromRgb(0xCA, 0xC4, 0xD0),
        Outline: Color.FromRgb(0x44, 0x47, 0x46),
        Primary: Color.FromRgb(0xD0, 0xBC, 0xFF),
        OnPrimary: Color.FromRgb(0x38, 0x1E, 0x72),
        PrimaryContainer: Color.FromRgb(0x4F, 0x37, 0x8B),
        OnPrimaryContainer: Color.FromRgb(0xEA, 0xDD, 0xFF),
        SecondaryContainer: Color.FromRgb(0x4A, 0x44, 0x58),
        OnSecondaryContainer: Color.FromRgb(0xE8, 0xDE, 0xF8),
        Error: Color.FromRgb(0xC9, 0x8B, 0x86),
        Dock: Color.FromArgb(0xE6, 0x20, 0x21, 0x24),
        OnDock: Color.FromRgb(0xF1, 0xF3, 0xF4),
        OnDockVariant: Color.FromRgb(0xC4, 0xC7, 0xC5),
        DockOutline: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
        HoverOverlay: Color.FromArgb(0x12, 0x00, 0x00, 0x00),
        ShadowOpacity: 0.35);

    private static ColorRoles LightRoles { get; } = new(
        Background: Color.FromRgb(0xF7, 0xF9, 0xFC),
        OnBackground: Color.FromRgb(0x30, 0x34, 0x3A),
        Surface: Color.FromRgb(0xF3, 0xF3, 0xFA),
        OnSurface: Color.FromRgb(0x1C, 0x1B, 0x1F),
        OnSurfaceVariant: Color.FromRgb(0x49, 0x45, 0x4F),
        Outline: Color.FromRgb(0xC4, 0xC7, 0xC5),
        Primary: Color.FromRgb(0x67, 0x50, 0xA4),
        OnPrimary: Colors.White,
        PrimaryContainer: Color.FromRgb(0xEA, 0xDD, 0xFF),
        OnPrimaryContainer: Color.FromRgb(0x21, 0x00, 0x5D),
        SecondaryContainer: Color.FromRgb(0xE8, 0xDE, 0xF8),
        OnSecondaryContainer: Color.FromRgb(0x1D, 0x19, 0x2B),
        Error: Color.FromRgb(0xB6, 0x5F, 0x58),
        Dock: Color.FromArgb(0xF0, 0xFC, 0xFC, 0xFD),
        OnDock: Color.FromRgb(0x3C, 0x40, 0x43),
        OnDockVariant: Color.FromRgb(0x5F, 0x63, 0x68),
        DockOutline: Color.FromArgb(0x2E, 0x20, 0x21, 0x24),
        HoverOverlay: Color.FromArgb(0x0D, 0x20, 0x21, 0x24),
        ShadowOpacity: 0.12);

    public static PluginThemePalette For(bool lightTheme) => lightTheme ? Light : Dark;

    internal static SettingsPalette Settings(bool lightTheme)
    {
        var theme = For(lightTheme);
        var roles = theme.Roles;
        var paper = roles.Background;
        var surface = lightTheme ? roles.Dock with { A = 255 } : roles.Surface;
        var wash = Composite(paper, WithAlpha(roles.PrimaryContainer, 0.25));
        return new SettingsPalette(
            Paper: paper,
            Surface: surface,
            Card: lightTheme ? surface : Composite(surface, WithAlpha(roles.OnSurface, 0.03)),
            Sidebar: Composite(paper, WithAlpha(wash, 0.8)),
            ScrollbarThumb: theme.SearchBrowserScrollbarThumb,
            Text: roles.OnSurface,
            Muted: roles.OnSurfaceVariant,
            Accent: roles.Primary,
            Line: WithAlpha(roles.OnSurface, 0.10),
            Hover: WithAlpha(roles.OnSurface, 0.05),
            Selected: WithAlpha(roles.OnSurface, 0.06),
            Wash: wash,
            HeroStart: Composite(paper, WithAlpha(roles.PrimaryContainer, 0.45)),
            HeroEnd: Composite(paper, WithAlpha(roles.PrimaryContainer, 0.12)),
            AccentLine: WithAlpha(roles.Primary, 0.18),
            KeycapBorder: WithAlpha(roles.OnSurface, 0.16),
            Scrim: WithAlpha(OpaqueBlack, 0.35));
    }

    internal static OnboardingPalette Onboarding(bool lightTheme)
    {
        var theme = For(lightTheme);
        return new OnboardingPalette(
            InactiveStep: WithAlpha(theme.Roles.OnSurface, 0.25),
            OnAccent: theme.Roles.OnPrimary,
            ToolbarSurface: theme.FloatingToolbar.Surface,
            ToolbarText: theme.FloatingToolbar.Text,
            ToolbarBorder: theme.FloatingToolbar.Border,
            ChipSurface: theme.SelectionChip.Surface,
            ChipText: theme.SelectionChip.Label,
            ChipBorder: theme.SelectionChip.NeutralOutline,
            TextHighlight: theme.TextInteraction.Selection);
    }

    internal static TwinDrillEggPalette TwinDrillEgg { get; } = new(
        Backdrop: Color.FromRgb(0x3A, 0x31, 0x42),
        Hair: Color.FromRgb(0xC8, 0x35, 0x4E),
        HairOutline: Color.FromRgb(0xA6, 0x2A, 0x42),
        Bangs: Color.FromRgb(0xD6, 0x3A, 0x55),
        Skin: Color.FromRgb(0xF6, 0xDC, 0xCF),
        Clothes: Color.FromRgb(0x26, 0x25, 0x2B),
        Ribbon: Color.FromRgb(0xF1, 0xF3, 0xF4),
        RibbonOutline: Color.FromRgb(0xC4, 0xC7, 0xC5));

    internal static Color TraceCardHover(bool lightTheme)
    {
        var roles = For(lightTheme).Roles;
        return Composite(roles.Surface, roles.HoverOverlay);
    }

    internal static Color TraceTimelineTrack(bool lightTheme)
    {
        var roles = For(lightTheme).Roles;
        return Composite(roles.Surface, WithAlpha(roles.Primary, 0.2));
    }

    public static Color SystemAccentFallback { get; } = Color.FromRgb(0x00, 0x78, 0xD4);
    public static Color Transparent { get; } = Colors.Transparent;
    public static Color OpaqueBlack { get; } = Colors.Black;
    public static Color SelectionDim { get; } = Color.FromArgb(0x59, 0x00, 0x00, 0x00);
    public static Color SelectionSheen { get; } = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
    public static Color SelectionHalo { get; } = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
    public static Color SelectionFrameFill { get; } = Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);
    public static Color EntranceParticle { get; } = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);
    public static Color ListeningText { get; } = Color.FromRgb(0xF4, 0xF5, 0xF8);
    public static Color ListeningTextOnLightBackdrop { get; } = Color.FromRgb(0x1F, 0x20, 0x23);
    public static Color ListeningShadowOnLightBackdrop { get; } = Colors.White;
    public static Color SceneRippleAudio { get; } = Color.FromRgb(0xC5, 0x9B, 0xFF);

    // The sample screens show some other app, so they keep the same colors in both themes.
    internal static OnboardingScreenPalette OnboardingScreen { get; } = new(
        Surface: Color.FromRgb(0x0F, 0x0F, 0x12),
        Border: Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
        Heading: Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF),
        Line: Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF),
        Text: Color.FromArgb(0xEB, 0xE8, 0xEA, 0xED),
        Selection: Colors.White,
        Dim: SelectionDim,
        Shadow: OpaqueBlack);

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
        Roles: DarkRoles,
        SearchBrowserScrollbarThumb: DarkRoles.OnBackground with { A = 0xA6 },
        SelectionChip: new SelectionChipPalette(
            Surface: DarkRoles.Dock,
            Label: DarkRoles.OnDock,
            Hint: DarkRoles.OnDockVariant,
            Icon: DarkRoles.OnDock,
            KeycapBackground: Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            KeycapBorder: Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF),
            KeycapText: Color.FromRgb(0xE8, 0xEA, 0xED),
            Divider: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            NeutralOutline: DarkRoles.DockOutline,
            ShadowDepth: 6,
            ShadowOpacity: DarkRoles.ShadowOpacity),
        MusicButton: MusicButtonFrom(DarkRoles),
        Provider: ProviderFrom(
            DarkRoles,
            trace: Color.FromRgb(0xE5, 0xE8, 0xFF),
            qwant: Color.FromRgb(0xE8, 0xEA, 0xED),
            neutral: Color.FromRgb(0xBD, 0xC1, 0xC6)),
        Card: CardFrom(DarkRoles),
        Toast: ToastFrom(DarkRoles),
        TextInteraction: TextInteractionFrom(DarkRoles),
        FloatingToolbar: FloatingToolbarFrom(
            DarkRoles,
            border: Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
            divider: Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            tagHighlightAlpha: 0.35,
            shadowOpacity: DarkRoles.ShadowOpacity),
        Translation: TranslationFrom(DarkRoles));

    private static PluginThemePalette Light { get; } = new(
        Roles: LightRoles,
        SearchBrowserScrollbarThumb: LightRoles.OnBackground with { A = 0x8F },
        SelectionChip: new SelectionChipPalette(
            Surface: LightRoles.Dock,
            Label: Color.FromRgb(0x1F, 0x20, 0x23),
            Hint: LightRoles.OnDockVariant,
            Icon: LightRoles.OnDock,
            KeycapBackground: Color.FromArgb(0x0D, 0x20, 0x21, 0x24),
            KeycapBorder: Color.FromArgb(0x24, 0x20, 0x21, 0x24),
            KeycapText: Color.FromRgb(0x3C, 0x40, 0x43),
            Divider: Color.FromArgb(0x29, 0x20, 0x21, 0x24),
            NeutralOutline: LightRoles.DockOutline,
            ShadowDepth: 8,
            ShadowOpacity: 0.3),
        MusicButton: MusicButtonFrom(LightRoles),
        Provider: ProviderFrom(
            LightRoles,
            trace: Color.FromRgb(0x45, 0x4A, 0x75),
            qwant: Color.FromRgb(0x28, 0x2B, 0x2F),
            neutral: LightRoles.OnDockVariant),
        Card: CardFrom(LightRoles),
        Toast: ToastFrom(LightRoles),
        TextInteraction: TextInteractionFrom(LightRoles),
        FloatingToolbar: FloatingToolbarFrom(
            LightRoles,
            border: Color.FromArgb(0x1A, 0x20, 0x21, 0x24),
            divider: Color.FromArgb(0x1F, 0x20, 0x21, 0x24),
            tagHighlightAlpha: 0.25,
            shadowOpacity: 0.14),
        Translation: TranslationFrom(LightRoles));

    private static Color DockHover(ColorRoles roles) => Composite(roles.Dock, roles.HoverOverlay);

    private static MusicButtonPalette MusicButtonFrom(ColorRoles roles) => new(
        Surface: roles.Dock,
        Foreground: roles.OnDock,
        Border: roles.DockOutline,
        Hover: DockHover(roles));

    private static TranslationPalette TranslationFrom(ColorRoles roles) => new(
        Surface: roles.Dock,
        Text: roles.OnDock,
        Border: roles.DockOutline,
        Hover: DockHover(roles));

    private static ProviderPalette ProviderFrom(ColorRoles roles, Color trace, Color qwant, Color neutral) => new(
        Surface: roles.Dock,
        Text: roles.OnDock,
        Hint: roles.OnDockVariant,
        Border: roles.DockOutline,
        Hover: DockHover(roles),
        MenuSurface: roles.Surface,
        MenuText: roles.OnSurface,
        MenuMutedText: roles.OnSurfaceVariant,
        MenuHover: roles.SecondaryContainer,
        MenuHoverText: roles.OnSecondaryContainer,
        MenuBorder: roles.Outline,
        MenuShadowOpacity: roles.ShadowOpacity,
        Google: Color.FromRgb(0x42, 0x85, 0xF4),
        GoogleRed: Color.FromRgb(0xEA, 0x43, 0x35),
        GoogleYellow: Color.FromRgb(0xFB, 0xBC, 0x05),
        GoogleGreen: Color.FromRgb(0x34, 0xA8, 0x53),
        Yandex: Color.FromRgb(0xFC, 0x3F, 0x1D),
        Trace: trace,
        Qwant: qwant,
        Neutral: neutral);

    private static CardPalette CardFrom(ColorRoles roles) => new(
        Surface: roles.Surface,
        Text: roles.OnSurface,
        MutedText: roles.OnSurfaceVariant,
        Border: roles.Outline,
        Primary: roles.Primary,
        PrimaryContainer: roles.PrimaryContainer,
        OnPrimaryContainer: roles.OnPrimaryContainer,
        SecondaryContainer: roles.SecondaryContainer,
        OnSecondaryContainer: roles.OnSecondaryContainer,
        ShadowOpacity: roles.ShadowOpacity);

    private static ToastPalette ToastFrom(ColorRoles roles) => new(
        Surface: roles.Surface,
        Text: roles.OnSurface,
        NeutralAccent: roles.Outline,
        ErrorAccent: roles.Error,
        SuccessAccent: roles.Primary,
        ShadowOpacity: roles.ShadowOpacity);

    private static TextInteractionPalette TextInteractionFrom(ColorRoles roles) => new(
        Hover: roles.Primary with { A = 0x24 },
        Selection: roles.Primary with { A = 0x55 });

    private static FloatingToolbarPalette FloatingToolbarFrom(
        ColorRoles roles, Color border, Color divider, double tagHighlightAlpha, double shadowOpacity) => new(
        Surface: roles.Surface,
        Text: roles.OnSurface,
        ButtonHover: roles.SecondaryContainer,
        Border: border,
        Divider: divider,
        Accent: roles.Primary,
        OnAccent: roles.OnPrimary,
        AccentHover: Composite(roles.Primary, WithAlpha(roles.OnPrimary, 0.08)),
        Tag: roles.SecondaryContainer,
        TagText: roles.OnSecondaryContainer,
        TagHighlight: Composite(roles.SecondaryContainer, WithAlpha(roles.Primary, tagHighlightAlpha)),
        ShadowOpacity: shadowOpacity);

    internal static double ContrastRatio(Color first, Color second)
    {
        static double Channel(byte value)
        {
            var srgb = value / 255d;
            return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var (lighter, darker) = (Luminance(first), Luminance(second));
        if (lighter < darker) (lighter, darker) = (darker, lighter);
        return (lighter + 0.05) / (darker + 0.05);
    }

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

internal sealed record SettingsPalette(
    Color Paper, Color Surface, Color Card, Color Sidebar, Color ScrollbarThumb,
    Color Text, Color Muted, Color Accent, Color Line, Color Hover, Color Selected,
    Color Wash, Color HeroStart, Color HeroEnd, Color AccentLine, Color KeycapBorder, Color Scrim);

internal sealed record OnboardingPalette(
    Color InactiveStep, Color OnAccent,
    Color ToolbarSurface, Color ToolbarText, Color ToolbarBorder,
    Color ChipSurface, Color ChipText, Color ChipBorder, Color TextHighlight);

internal sealed record OnboardingScreenPalette(
    Color Surface, Color Border, Color Heading, Color Line, Color Text, Color Selection, Color Dim, Color Shadow);

internal sealed record TwinDrillEggPalette(
    Color Backdrop, Color Hair, Color HairOutline, Color Bangs, Color Skin, Color Clothes, Color Ribbon, Color RibbonOutline);

// Surface roles are the opaque Material cards and menus; Dock roles are the translucent
// neutral chips and buttons that float directly over the screenshot.
internal sealed record ColorRoles(
    Color Background,
    Color OnBackground,
    Color Surface,
    Color OnSurface,
    Color OnSurfaceVariant,
    Color Outline,
    Color Primary,
    Color OnPrimary,
    Color PrimaryContainer,
    Color OnPrimaryContainer,
    Color SecondaryContainer,
    Color OnSecondaryContainer,
    Color Error,
    Color Dock,
    Color OnDock,
    Color OnDockVariant,
    Color DockOutline,
    Color HoverOverlay,
    double ShadowOpacity);

internal sealed record PluginThemePalette(
    ColorRoles Roles,
    Color SearchBrowserScrollbarThumb,
    SelectionChipPalette SelectionChip,
    MusicButtonPalette MusicButton,
    ProviderPalette Provider,
    CardPalette Card,
    ToastPalette Toast,
    TextInteractionPalette TextInteraction,
    FloatingToolbarPalette FloatingToolbar,
    TranslationPalette Translation);

internal sealed record TextInteractionPalette(
    Color Hover,
    Color Selection);

internal sealed record FloatingToolbarPalette(
    Color Surface,
    Color Text,
    Color ButtonHover,
    Color Border,
    Color Divider,
    Color Accent,
    Color OnAccent,
    Color AccentHover,
    Color Tag,
    Color TagText,
    Color TagHighlight,
    double ShadowOpacity);

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
    Color Qwant,
    Color Neutral);

internal sealed record CardPalette(
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
