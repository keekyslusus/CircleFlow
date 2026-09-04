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
        "m475-80 185-480h79L924-80h-65l-45-117H584L539-80h-64ZM162-201l-42-42 201-201q-51-53-85.5-107.5T183-660h65q16 43 43.5 85t72.5 88q46-48 85-117.5T505-740H40v-60h290v-80h60v80h290v60H567q-17 78-61.5 159.5T406-443l102 104-24 63-121-125-201 200Zm443-51h188l-94-248-94 248Z");
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
        return new TranslationOverlayVisual(new Canvas { IsHitTestVisible = false }, consent, proceed, cancel);
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
