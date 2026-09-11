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
    internal static readonly Geometry TranslateIconGeometry = OverlayVisualResources.FrozenGeometry(
        "M12.87 15.07l-2.54-2.51.03-.03c1.74-1.94 2.98-4.17 3.71-6.53H17V4h-7V2H8v2H1v1.99h11.17C11.5 7.92 10.44 9.75 9 11.35 8.07 10.32 7.3 9.19 6.69 8h-2c.73 1.63 1.73 3.17 2.98 4.56l-5.09 5.02L4 19l5-5 3.11 3.11.76-2.04zM18.5 10h-2L12 22h2l1.12-3h4.75L21 22h2l-4.5-12zm-2.62 7l1.62-4.33L19.12 17h-3.24z");
    internal static readonly Geometry ShowOriginalIconGeometry = OverlayVisualResources.FrozenGeometry(
        "M259-200v-60h310q70 0 120.5-46.5T740-422q0-69-50.5-115.5T569-584H274l114 114-42 42-186-186 186-186 42 42-114 114h294q95 0 163.5 64T800-422q0 94-68.5 158T568-200H259Z");

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

    internal static TranslationActionVisual CreateTranslationAction(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).Translation;
        var icon = new Path
        {
            Data = TranslateIconGeometry,
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

    internal static TranslationOverlayVisual CreateTranslationOverlay(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).TextInteraction;
        var title = new TextBlock
        {
            Text = strings.TranslationConsentTitle,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = OverlayVisualResources.Frozen(palette.CardText),
            TextWrapping = TextWrapping.Wrap,
        };
        var message = new TextBlock
        {
            Text = strings.TranslationConsentMessage,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
            Foreground = OverlayVisualResources.Frozen(palette.CardText),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 14),
        };
        var proceed = CreateCardButton(strings.Continue, palette);
        var cancel = CreateCardButton(strings.ConsentCancel, palette);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(proceed);
        var content = new StackPanel { Width = 360 };
        content.Children.Add(title);
        content.Children.Add(message);
        content.Children.Add(buttons);
        var consent = new Border
        {
            Child = content,
            Background = OverlayVisualResources.Frozen(palette.CardSurface),
            BorderBrush = OverlayVisualResources.Frozen(palette.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(proceed, strings.Continue);
        AutomationProperties.SetName(cancel, strings.ConsentCancel);
        return new TranslationOverlayVisual(consent, proceed, cancel);
    }

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
