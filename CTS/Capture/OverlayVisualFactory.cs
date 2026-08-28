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
    Path DimRect,
    Path Sheen,
    Polyline Halo,
    Polyline Accent,
    Path SelectionFrame,
    Border Chip,
    TranslateTransform ChipLift);

// Composes the overlay visual tree layer by layer; the window only keeps references.
public static class OverlayVisualFactory
{
    private const double HaloThickness = 12;
    private const double HaloBlurRadius = 8;
    private const double AccentThickness = 2.5;
    private const double DimBlurRadius = 28;
    // The reveal's outer figure must sit well past the window edges, or the blur softens
    // the screen borders instead of just the selection boundary.
    private const double RevealBleed = 96;
    private const double SheenBlurRadius = 14;
    private const double FrameCornerRadius = 6;
    private const double FrameGlowRadius = 18;
    private const double ChipEntranceLift = 24;

    // Tweak point for the chip outline width, in DIPs.
    private const double ChipBorderThicknessDips = 1;
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(150);

    public static OverlayVisual CreateRoot(
        BitmapSource? frame,
        Size size,
        double chipBottomMargin,
        UiStrings strings) =>
        CreateRoot(frame, size, chipBottomMargin, SystemTheme.IsLight(), strings);

    internal static OverlayVisual CreateRoot(
        BitmapSource? frame,
        Size size,
        double chipBottomMargin,
        bool lightTheme,
        UiStrings strings)
    {
        var screenshot = new Image { Source = frame, Stretch = Stretch.Fill, IsHitTestVisible = true };

        // Blurring the dim itself is what melts the boundary between the dimmed desktop
        // and the revealed lasso interior into a wide gradient.
        var dim = new Path
        {
            Fill = Frozen(PluginPalette.SelectionDim),
            Data = BuildRevealGeometry(size, []),
            IsHitTestVisible = false,
        };
        if (HardwareEffectsEnabled()) dim.Effect = new BlurEffect { Radius = DimBlurRadius };

        // Final-rectangle twin of the dim layer; the window cross-fades Dim into it on
        // mouse-up. Linear opacities sum to a constant dim, so the retraction from the
        // lasso outline to the rectangle reads as one seamless move.
        var dimRect = new Path
        {
            Fill = Frozen(PluginPalette.SelectionDim),
            Data = Geometry.Empty,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        if (HardwareEffectsEnabled()) dimRect.Effect = new BlurEffect { Radius = DimBlurRadius };

        // Light translucent fill inside the lasso; the blur softens its contours so the
        // interior glows instead of showing a hard polygon edge.
        var sheen = new Path
        {
            Fill = Frozen(PluginPalette.SelectionSheen),
            Data = Geometry.Empty,
            IsHitTestVisible = false,
        };
        if (HardwareEffectsEnabled()) sheen.Effect = new BlurEffect { Radius = SheenBlurRadius };

        var accentColor = SystemAccentColor.Read();
        // Blurred so the halo reads as a soft light glow under the crisp accent line
        // instead of a hard-edged band on the selection boundary.
        var halo = new Polyline
        {
            Stroke = Frozen(PluginPalette.SelectionHalo),
            StrokeThickness = HaloThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        if (HardwareEffectsEnabled()) halo.Effect = new BlurEffect { Radius = HaloBlurRadius };

        var accent = new Polyline
        {
            Stroke = Frozen(accentColor),
            StrokeThickness = AccentThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };

        var selectionFrame = new Path
        {
            Fill = Frozen(PluginPalette.SelectionFrameFill),
            Stroke = Frozen(accentColor),
            StrokeThickness = AccentThickness,
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = accentColor,
                BlurRadius = FrameGlowRadius,
                ShadowDepth = 0,
                Opacity = 0.7,
            },
        };

        var lift = new TranslateTransform();
        var chip = CreateChip(lift, PluginPalette.For(lightTheme).SelectionChip, strings);
        chip.VerticalAlignment = VerticalAlignment.Bottom;
        chip.HorizontalAlignment = HorizontalAlignment.Center;
        chip.Margin = new Thickness(0, 0, 0, chipBottomMargin);

        var root = new Grid();
        root.Children.Add(screenshot);
        root.Children.Add(dim);
        root.Children.Add(dimRect);
        root.Children.Add(sheen);
        root.Children.Add(halo);
        root.Children.Add(accent);
        root.Children.Add(selectionFrame);
        root.Children.Add(chip);

        return new OverlayVisual(root, screenshot, dim, dimRect, sheen, halo, accent, selectionFrame, chip, lift);
    }

    // Even-odd of an oversized monitor rectangle and the lasso polygon: the polygon
    // interior gets its brightness back, everything else stays dimmed. The outer figure
    // bleeds past the window so the blur only rounds the selection boundary.
    public static Geometry BuildRevealGeometry(Size size, IReadOnlyList<Point> polygon)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(-RevealBleed, -RevealBleed), true, true);
            context.PolyLineTo(
                [
                    new Point(size.Width + RevealBleed, -RevealBleed),
                    new Point(size.Width + RevealBleed, size.Height + RevealBleed),
                    new Point(-RevealBleed, size.Height + RevealBleed),
                ],
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

    // Closed fill of the lasso polygon; Nonzero keeps self-intersecting loops filled.
    public static Geometry BuildPolygonGeometry(IReadOnlyList<Point> polygon)
    {
        if (polygon.Count < 2) return Geometry.Empty;
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            var rest = new Point[polygon.Count - 1];
            for (var i = 1; i < polygon.Count; i++) rest[i - 1] = polygon[i];
            context.BeginFigure(polygon[0], true, true);
            context.PolyLineTo(rest, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    public static Geometry BuildSelectionFrameGeometry(Rect rect)
    {
        var geometry = new RectangleGeometry(rect, FrameCornerRadius, FrameCornerRadius);
        geometry.Freeze();
        return geometry;
    }

    // Circle-to-search snap: the lasso layers melt into the rectangle that is actually
    // sent, so the region leaves the screen exactly as it entered the provider.
    public static void BeginSelectionReveal(OverlayVisual visual, Geometry revealGeometry, Geometry frameGeometry)
    {
        visual.DimRect.Data = revealGeometry;
        visual.SelectionFrame.Data = frameGeometry;
        if (!AnimationsEnabled())
        {
            visual.Dim.Opacity = 0;
            visual.Sheen.Opacity = 0;
            visual.Halo.Opacity = 0;
            visual.Accent.Opacity = 0;
            visual.DimRect.Opacity = 1;
            visual.SelectionFrame.Opacity = 1;
            return;
        }

        visual.Dim.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, RevealDuration));
        visual.Sheen.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, RevealDuration));
        visual.Halo.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, RevealDuration));
        visual.Accent.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, RevealDuration));
        visual.DimRect.BeginAnimation(UIElement.OpacityProperty, Animate(0, 1, RevealDuration));
        visual.SelectionFrame.BeginAnimation(UIElement.OpacityProperty, Animate(0, 1, RevealDuration));
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

    // A software-rendered blur rebuilds a full-screen bitmap on every mouse move.
    internal static bool HardwareEffectsEnabled() => RenderCapability.Tier >> 16 >= 2;

    private static Border CreateChip(TranslateTransform lift, SelectionChipPalette palette, UiStrings strings)
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
            Text = strings.SelectionPrompt,
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
                Text = strings.CancelKeyName,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Frozen(palette.KeycapText),
            },
        };
        var hint = new TextBlock
        {
            Text = strings.CancelAction,
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
                Color = PluginPalette.OpaqueBlack,
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

    // High Contrast themes suppress the accent; a neutral outline stays readable there.
    private static Brush ChipOutlineBrush(SelectionChipPalette palette) =>
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
