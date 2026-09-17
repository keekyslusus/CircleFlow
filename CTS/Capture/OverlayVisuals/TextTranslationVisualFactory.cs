namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal static class TextTranslationVisualFactory
{
    internal static TextSelectionVisual CreateTextSelection(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).TextInteraction;
        var copy = CreateCardButton(strings.TextCopy, palette);
        var search = CreateCardButton(strings.TextSearch, palette);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(copy);
        row.Children.Add(search);
        var card = new Border
        {
            Child = row,
            Background = OverlayVisualResources.Frozen(palette.CardSurface),
            BorderBrush = OverlayVisualResources.Frozen(palette.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
            Visibility = Visibility.Collapsed,
        };
        var actionLayer = new Canvas { Background = null, IsHitTestVisible = true };
        actionLayer.Children.Add(card);
        return new TextSelectionVisual(
            new Canvas { IsHitTestVisible = false },
            actionLayer,
            card,
            copy,
            search);
    }

    internal static ImageActionCardVisual CreateImageActionCard(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).TextInteraction;
        var copy = CreateCardButton(strings.TextCopy, palette);
        var save = CreateCardButton(strings.ImageSave, palette);
        var search = CreateCardButton(strings.TextSearch, palette);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(copy);
        row.Children.Add(save);
        row.Children.Add(search);
        var card = new Border
        {
            Child = row,
            Background = OverlayVisualResources.Frozen(palette.CardSurface),
            BorderBrush = OverlayVisualResources.Frozen(palette.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
            Visibility = Visibility.Collapsed,
        };
        var actionLayer = new Canvas { Background = null, IsHitTestVisible = true };
        actionLayer.Children.Add(card);
        return new ImageActionCardVisual(
            actionLayer,
            card,
            copy,
            save,
            search);
    }

    internal static TranslationActionVisual CreateTranslationAction(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).Translation;
        var icon = new Path
        {
            Data = PluginIcons.TranslateFilled,
            Fill = OverlayVisualResources.Frozen(palette.Text),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        icon.RenderTransform = new ScaleTransform(1, 1);
        var loading = new LoadingIndicatorVisual
        {
            Fill = OverlayVisualResources.Frozen(palette.Text),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            RenderTransform = new ScaleTransform(0.72, 0.72),
        };
        var glyph = new Grid { Width = 42, Height = 42 };
        glyph.Children.Add(icon);
        glyph.Children.Add(loading);
        var button = new Button
        {
            Content = glyph,
            Width = 44,
            Height = 44,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = strings.Translate,
            Focusable = true,
            Effect = OverlayVisualResources.DockShadow(
                palette.Surface.A == 0xF0 ? 8 : 6,
                palette.Surface.A == 0xF0 ? 0.3 : 0.35),
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 22, palette.Hover, palette.Text);
        AutomationProperties.SetName(button, strings.Translate);
        return new TranslationActionVisual(button, icon, loading);
    }

    internal static TranslationOverlayVisual CreateTranslationOverlay() => new(new Grid
    {
        Visibility = Visibility.Collapsed,
        Opacity = 0,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
    });

    private static Button CreateCardButton(string text, TextInteractionPalette palette)
    {
        var button = new Button
        {
            Content = text,
            Foreground = OverlayVisualResources.Frozen(palette.CardText),
            Background = OverlayVisualResources.Frozen(palette.CardSurface),
            BorderThickness = new Thickness(),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(2),
            Cursor = Cursors.Hand,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 8, palette.ButtonHover, palette.CardText);
        AutomationProperties.SetName(button, text);
        return button;
    }
}
