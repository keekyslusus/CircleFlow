namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal static class ActionTrayVisualFactory
{
    internal const double ChipMinHeight = 44;
    private const double ChipBorderThicknessDips = 1;

    internal static ActionTrayVisual Create(
        SelectionChipPalette palette,
        UiStrings strings,
        ProviderMenuVisual? provider,
        MusicOverlayVisual music,
        TranslationActionVisual translation)
    {
        var lift = new TranslateTransform();
        var (chip, prompt, hint) = CreateChip(palette, strings);
        var tray = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransform = lift,
        };
        tray.Children.Add(chip);
        if (provider is not null) tray.Children.Add(provider.Button);
        tray.Children.Add(translation.Button);
        tray.Children.Add(music.Button);

        return new ActionTrayVisual(tray, chip, lift) { Prompt = prompt, Hint = hint };
    }

    private static (Border Chip, TextBlock Prompt, SelectionHintVisual Hint) CreateChip(SelectionChipPalette palette, UiStrings strings)
    {
        var icon = new Path
        {
            Data = PluginIcons.SelectAreaFilled,
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
            FontFamily = PluginTypography.Font,
            FontSize = PluginTypography.Body,
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
        var hintKeys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var hintAction = new TextBlock
        {
            FontFamily = PluginTypography.Font,
            FontSize = PluginTypography.Caption,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = OverlayVisualResources.Frozen(palette.Hint),
        };
        var hintContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        hintContent.Children.Add(hintKeys);
        hintContent.Children.Add(hintAction);
        var hint = new SelectionHintVisual(
            hintContent,
            hintKeys,
            hintAction,
            OverlayVisualResources.Frozen(palette.KeycapBackground),
            OverlayVisualResources.Frozen(palette.KeycapBorder),
            OverlayVisualResources.Frozen(palette.KeycapText));
        SelectionHintVisualPresenter.Show(hint, SelectionHint.EscapeCancel, strings, animate: false);

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(divider);
        row.Children.Add(hintContent);

        var background = OverlayVisualResources.Frozen(palette.Surface);
        var outline = ChipOutlineBrush(palette);
        var shadow = new Border
        {
            Background = background,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 20,
                ShadowDepth = palette.ShadowDepth,
                Direction = -90,
                Opacity = palette.ShadowOpacity,
            },
        };
        var surface = new Border
        {
            Child = row,
            MinHeight = ChipMinHeight,
            Padding = new Thickness(18, 8, 16, 8),
            Background = background,
            BorderBrush = outline,
            BorderThickness = new Thickness(ChipBorderThicknessDips),
        };
        var layers = new Grid();
        layers.Children.Add(shadow);
        layers.Children.Add(surface);
        var chip = new Border
        {
            Child = layers,
            VerticalAlignment = VerticalAlignment.Center,
        };
        void UpdateRadius()
        {
            var radius = new CornerRadius(chip.ActualHeight / 2);
            shadow.CornerRadius = radius;
            surface.CornerRadius = radius;
        }
        shadow.CornerRadius = new CornerRadius(22);
        surface.CornerRadius = new CornerRadius(22);
        chip.SizeChanged += (_, _) => UpdateRadius();
        return (chip, label, hint);
    }

    private static Brush ChipOutlineBrush(SelectionChipPalette palette) =>
        SystemParameters.HighContrast
            ? OverlayVisualResources.Frozen(palette.NeutralOutline)
            : OverlayVisualResources.Frozen(SystemAccentColor.Read());
}
