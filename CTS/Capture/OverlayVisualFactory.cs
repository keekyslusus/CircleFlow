namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CircleToSearch.Ui;

public sealed record OverlayVisual(
    Grid Root,
    Image Screenshot,
    Path Dim,
    Polyline Halo,
    Polyline Accent,
    Border Chip,
    TranslateTransform ChipLift);

// Composes the overlay visual tree layer by layer; the window only keeps references.
public static class OverlayVisualFactory
{
    private const double DimOpacity = 0.35;
    private const double HaloThickness = 9;
    private const double AccentThickness = 2.5;
    private const double ChipEntranceLift = 24;

    // Tweak point for the chip outline width, in DIPs.
    private const double ChipBorderThicknessDips = 1;
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);

    public static OverlayVisual CreateRoot(BitmapSource? frame, Size size, double chipBottomMargin) =>
        CreateRoot(frame, size, chipBottomMargin, SystemTheme.IsLight());

    internal static OverlayVisual CreateRoot(BitmapSource frame, Size size, double chipBottomMargin, bool lightTheme)
    {
        var screenshot = new Image { Source = frame, Stretch = Stretch.Fill, IsHitTestVisible = true };

        var dim = new Path
        {
            Fill = Frozen(Color.FromArgb((byte)(255 * DimOpacity), 0, 0, 0)),
            Data = BuildRevealGeometry(size, []),
            IsHitTestVisible = false,
        };

        var halo = new Polyline
        {
            Stroke = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = HaloThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };

        var accent = new Polyline
        {
            Stroke = UiPalette.SelectionGradient,
            StrokeThickness = AccentThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };

        var lift = new TranslateTransform();
        var chip = CreateChip(lift, lightTheme ? LightPalette : DarkPalette);
        chip.VerticalAlignment = VerticalAlignment.Bottom;
        chip.HorizontalAlignment = HorizontalAlignment.Center;
        chip.Margin = new Thickness(0, 0, 0, chipBottomMargin);

        var root = new Grid();
        root.Children.Add(screenshot);
        root.Children.Add(dim);
        root.Children.Add(halo);
        root.Children.Add(accent);
        root.Children.Add(chip);

        return new OverlayVisual(root, screenshot, dim, halo, accent, chip, lift);
    }

    // Even-odd of the full monitor rectangle and the lasso polygon: the polygon interior
    // gets its brightness back, everything else stays dimmed.
    public static Geometry BuildRevealGeometry(Size size, IReadOnlyList<Point> polygon)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 0), true, true);
            context.PolyLineTo(
                [new Point(size.Width, 0), new Point(size.Width, size.Height), new Point(0, size.Height)],
                true,
                true);
            if (polygon.Count >= 2)
            {
                var rest = new Point[polygon.Count - 1];
                for (var i = 1; i < polygon.Count; i++) rest[i - 1] = polygon[i];
                context.BeginFigure(polygon[0], true, true);
                context.PolyLineTo(rest, true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    public static void BeginChipEntrance(OverlayVisual visual)
    {
        if (!AnimationsEnabled())
        {
            visual.Chip.Opacity = 1;
            visual.ChipLift.Y = 0;
            return;
        }

        visual.Chip.BeginAnimation(UIElement.OpacityProperty, Animate(0, 1, EntranceDuration));
        visual.ChipLift.BeginAnimation(TranslateTransform.YProperty, Animate(ChipEntranceLift, 0, EntranceDuration));
    }

    public static void BeginChipExit(OverlayVisual visual)
    {
        visual.Chip.IsHitTestVisible = false;
        if (!AnimationsEnabled())
        {
            visual.Chip.Opacity = 0;
            return;
        }

        visual.Chip.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, ExitDuration));
    }

    private static DoubleAnimation Animate(double from, double to, TimeSpan duration) =>
        new(from, to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    internal static bool AnimationsEnabled() =>
        SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    private static Border CreateChip(TranslateTransform lift, ChipPalette palette)
    {
        var icon = new Path
        {
            Data = ChipIconGeometry,
            Fill = Frozen(palette.Icon),
            Width = 13,
            Height = 13,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var label = new TextBlock
        {
            Text = UiStrings.SelectionPrompt,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Frozen(palette.Label),
        };
        var divider = new Rectangle
        {
            Width = 1,
            Height = 18,
            Fill = Frozen(palette.Divider),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        var keycap = new Border
        {
            Background = Frozen(palette.KeycapBackground),
            BorderBrush = Frozen(palette.KeycapBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 6, 7, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = UiStrings.CancelKeyName,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Frozen(palette.KeycapText),
            },
        };
        var hint = new TextBlock
        {
            Text = UiStrings.CancelAction,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Frozen(palette.Hint),
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(divider);
        row.Children.Add(keycap);
        row.Children.Add(hint);

        var chip = new Border
        {
            Child = row,
            MinHeight = 44,
            Padding = new Thickness(18, 8, 16, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Frozen(palette.Surface),
            BorderBrush = ChipOutlineBrush(palette),
            BorderThickness = new Thickness(ChipBorderThicknessDips),
            RenderTransform = lift,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = palette.ShadowDepth,
                Direction = -90,
                Opacity = palette.ShadowOpacity,
            },
        };
        // Font metrics vary by locale, so the height is not known until layout; WPF also does
        // not clamp oversized radii into a pill (unlike CSS) — keep it at half the real height.
        chip.CornerRadius = new CornerRadius(22);
        chip.SizeChanged += (_, _) => chip.CornerRadius = new CornerRadius(chip.ActualHeight / 2);
        return chip;
    }

    private sealed record ChipPalette(
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

    private static readonly ChipPalette DarkPalette = new(
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
        ShadowOpacity: 0.35);

    private static readonly ChipPalette LightPalette = new(
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
        ShadowOpacity: 0.3);

    // High Contrast themes suppress the accent; a neutral outline stays readable there.
    private static Brush ChipOutlineBrush(ChipPalette palette) =>
        SystemParameters.HighContrast
            ? Frozen(palette.NeutralOutline)
            : Frozen(SystemAccentColor.Read());

    // Material Symbols "ink_selection" (Apache-2.0); path data taken verbatim from
    // Images/ink_selection.svg (fill icon, viewBox 0 -960 960 960).
    private static readonly Geometry ChipIconGeometry = CreateChipIconGeometry();

    private static Geometry CreateChipIconGeometry()
    {
        var geometry = Geometry.Parse(
            "M439-120v-401h401v60H542l298 298-43 43-298-298v298h-60Z" +
            "m-154 0v-60h60v60h-60Z" +
            "M180-780h-60q0-24.75 17.63-42.38Q155.25-840 180-840v60Z" +
            "m105 0v-60h60v60h-60Z" +
            "m165 0v-60h60v60h-60Z" +
            "m165 0v-60h60v60h-60Z" +
            "m165 0v-60q24.75 0 42.38 17.62Q840-804.75 840-780h-60Z" +
            "M180-180v60q-24.75 0-42.37-17.63Q120-155.25 120-180h60Z" +
            "m-60-105v-60h60v60h-60Z" +
            "m0-165v-60h60v60h-60Z" +
            "m0-165v-60h60v60h-60Z" +
            "m660 0v-60h60v60h-60Z");
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
