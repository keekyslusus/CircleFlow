namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal static class ActionTrayVisualFactory
{
    private const double ChipBorderThicknessDips = 1;
    private static readonly Geometry ChipIconGeometry = CreateChipIconGeometry();

    internal static ActionTrayVisual Create(
        double chipBottomMargin,
        SelectionChipPalette palette,
        UiStrings strings,
        ProviderMenuVisual? provider,
        MusicOverlayVisual music)
    {
        var lift = new TranslateTransform();
        var chip = CreateChip(palette, strings);
        var tray = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, chipBottomMargin),
            RenderTransform = lift,
        };
        tray.Children.Add(chip);
        if (provider is not null) tray.Children.Add(provider.Button);
        tray.Children.Add(music.Button);

        var providerMenuLayer = new Canvas();
        if (provider is not null) providerMenuLayer.Children.Add(provider.Menu);
        var root = new Grid { IsHitTestVisible = true };
        root.Children.Add(tray);
        root.Children.Add(providerMenuLayer);
        Panel.SetZIndex(root, 2);
        return new ActionTrayVisual(root, tray, chip, lift);
    }

    private static Border CreateChip(SelectionChipPalette palette, UiStrings strings)
    {
        var icon = new Path
        {
            Data = ChipIconGeometry,
            Fill = OverlayVisualResources.Frozen(palette.Icon),
            Width = 13,
            Height = 13,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var label = new TextBlock
        {
            Text = strings.SelectionPrompt,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = OverlayVisualResources.Frozen(palette.Label),
        };
        var divider = new Rectangle
        {
            Width = 1,
            Height = 18,
            Fill = OverlayVisualResources.Frozen(palette.Divider),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        var keycap = new Border
        {
            Background = OverlayVisualResources.Frozen(palette.KeycapBackground),
            BorderBrush = OverlayVisualResources.Frozen(palette.KeycapBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 6, 7, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = strings.CancelKeyName,
                FontFamily = OverlayVisualResources.Font,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = OverlayVisualResources.Frozen(palette.KeycapText),
            },
        };
        var hint = new TextBlock
        {
            Text = strings.CancelAction,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = OverlayVisualResources.Frozen(palette.Hint),
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
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
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = ChipOutlineBrush(palette),
            BorderThickness = new Thickness(ChipBorderThicknessDips),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 20,
                ShadowDepth = palette.ShadowDepth,
                Direction = -90,
                Opacity = palette.ShadowOpacity,
            },
        };
        chip.CornerRadius = new CornerRadius(22);
        chip.SizeChanged += (_, _) => chip.CornerRadius = new CornerRadius(chip.ActualHeight / 2);
        return chip;
    }

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

    private static Brush ChipOutlineBrush(SelectionChipPalette palette) =>
        SystemParameters.HighContrast
            ? OverlayVisualResources.Frozen(palette.NeutralOutline)
            : OverlayVisualResources.Frozen(SystemAccentColor.Read());
}
