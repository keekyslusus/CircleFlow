namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Translation;
using CircleToSearch.Ui;

public sealed class TranslationOverlayVisual
{
    private static readonly Geometry TranslateIconGeometry = CreateTranslateIconGeometry();
    private static readonly Geometry CloseIconGeometry = CreateCloseIconGeometry();

    public Canvas CardsLayer { get; }
    public Button TranslateButton { get; }
    public Path TranslateIcon { get; }
    public LoadingIndicatorVisual LoadingIndicator { get; }
    public Border TogglePill { get; }
    public Button ToggleButton { get; }
    public TextBlock ToggleButtonText { get; }
    public Button ClosePillButton { get; }

    public TranslationOverlayVisual(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme);

        CardsLayer = new Canvas
        {
            IsHitTestVisible = true,
            Visibility = Visibility.Collapsed,
            Opacity = 0,
        };

        TranslateIcon = new Path
        {
            Data = TranslateIconGeometry,
            Fill = OverlayVisualResources.Frozen(palette.TranslateButton.Foreground),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };

        LoadingIndicator = new LoadingIndicatorVisual
        {
            Fill = OverlayVisualResources.Frozen(palette.TranslateButton.Foreground),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            RenderTransform = new ScaleTransform(0.72, 0.72),
        };

        var glyph = new Grid { Width = 42, Height = 42 };
        glyph.Children.Add(TranslateIcon);
        glyph.Children.Add(LoadingIndicator);

        TranslateButton = new Button
        {
            Content = glyph,
            Width = 44,
            Height = 44,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = OverlayVisualResources.Frozen(palette.TranslateButton.Surface),
            Foreground = OverlayVisualResources.Frozen(palette.TranslateButton.Foreground),
            BorderBrush = OverlayVisualResources.Frozen(palette.TranslateButton.Border),
            BorderThickness = new Thickness(1),
            ToolTip = strings.TranslateScreenAction,
            Focusable = true,
            Cursor = Cursors.Hand,
            Effect = OverlayVisualResources.DockShadow(
                palette.TranslateButton.Surface.A == 0xF0 ? 8 : 6,
                palette.TranslateButton.Surface.A == 0xF0 ? 0.3 : 0.35),
        };
        OverlayVisualResources.ApplyButtonTemplate(
            TranslateButton, 22, palette.TranslateButton.Hover, palette.TranslateButton.Foreground);
        AutomationProperties.SetName(TranslateButton, strings.TranslateScreenAction);

        ToggleButtonText = new TextBlock
        {
            Text = strings.ShowOriginal,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = OverlayVisualResources.Frozen(palette.Translation.TogglePillText),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToggleButton = new Button
        {
            Content = ToggleButtonText,
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand,
            Focusable = false,
        };
        OverlayVisualResources.ApplyButtonTemplate(
            ToggleButton, 14, palette.Translation.TogglePillHover, palette.Translation.TogglePillText);

        var closeIcon = new Path
        {
            Data = CloseIconGeometry,
            Fill = OverlayVisualResources.Frozen(palette.Translation.TogglePillText),
            Width = 10,
            Height = 10,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ClosePillButton = new Button
        {
            Content = closeIcon,
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            Focusable = false,
            ToolTip = strings.Close,
        };
        OverlayVisualResources.ApplyButtonTemplate(
            ClosePillButton, 14, palette.Translation.TogglePillHover, palette.Translation.TogglePillText);

        var pillStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pillStack.Children.Add(ToggleButton);
        pillStack.Children.Add(new Rectangle
        {
            Width = 1,
            Height = 14,
            Fill = OverlayVisualResources.Frozen(palette.Translation.TogglePillBorder),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
        });
        pillStack.Children.Add(ClosePillButton);

        TogglePill = new Border
        {
            Child = pillStack,
            Height = 36,
            Padding = new Thickness(4, 2, 4, 2),
            Background = OverlayVisualResources.Frozen(palette.Translation.TogglePillSurface),
            BorderBrush = OverlayVisualResources.Frozen(palette.Translation.TogglePillBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 16,
                ShadowDepth = 4,
                Direction = -90,
                Opacity = palette.Translation.ShadowOpacity,
            },
        };
    }

    public void PopulateCards(
        IReadOnlyList<TranslationBlock> blocks,
        Action<string> onCardClicked)
    {
        CardsLayer.Children.Clear();
        foreach (var block in blocks)
        {
            var card = new Border
            {
                Background = OverlayVisualResources.Frozen(block.BackgroundColor),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(3, 1, 3, 1),
                Width = Math.Max(0, block.DipBounds.Width + 6),
                MinHeight = Math.Max(0, block.DipBounds.Height + 2),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = block.TranslatedText,
                    Foreground = OverlayVisualResources.Frozen(block.TextColor),
                    FontSize = block.FontSize,
                    FontFamily = OverlayVisualResources.Font,
                    FontWeight = block.FontWeight,
                    TextWrapping = TextWrapping.Wrap,
                },
            };

            var textToCopy = block.TranslatedText;
            card.MouseLeftButtonDown += (s, e) =>
            {
                onCardClicked(textToCopy);
                e.Handled = true;
            };

            Canvas.SetLeft(card, block.DipBounds.X - 3);
            Canvas.SetTop(card, block.DipBounds.Y - 1);
            CardsLayer.Children.Add(card);
        }
    }

    public void SetLoading(bool loading)
    {
        if (loading)
        {
            TranslateIcon.Visibility = Visibility.Collapsed;
            LoadingIndicator.Visibility = Visibility.Visible;
            LoadingIndicator.Opacity = 1;
        }
        else
        {
            LoadingIndicator.Visibility = Visibility.Collapsed;
            LoadingIndicator.Opacity = 0;
            TranslateIcon.Visibility = Visibility.Visible;
        }
    }

    private static Geometry CreateTranslateIconGeometry()
    {
        var g = Geometry.Parse("M12.87 15.07l-2.54-2.51.03-.08c1.74-1.94 2.98-4.17 3.71-6.49H17V4h-7V2H8v2H1v1.99h11.17C11.5 7.92 10.44 9.75 9 11.35 8.07 10.32 7.3 9.19 6.69 8h-2c.73 1.63 1.73 3.17 2.98 4.56l-5.09 5.02L4 19l5-5 3.11 3.11.76-2.04zM18.5 10h-2L12 22h2l1.12-3h4.75L21 22h2l-4.5-12zm-2.62 7l1.62-4.33L19.12 17h-3.24z");
        g.Freeze();
        return g;
    }

    private static Geometry CreateCloseIconGeometry()
    {
        var g = Geometry.Parse("M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z");
        g.Freeze();
        return g;
    }
}
