namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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

    internal static TranslationActionVisual CreateTranslationAction(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).Translation;
        var label = new TextBlock
        {
            Text = strings.Translate,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var loading = new LoadingIndicatorVisual
        {
            Width = 22,
            Height = 22,
            Fill = OverlayVisualResources.Frozen(palette.Text),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(loading);
        content.Children.Add(label);
        var button = new Button
        {
            Content = content,
            Height = 44,
            MinWidth = 92,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 0, 14, 0),
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = strings.Translate,
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 22, palette.Hover, palette.Text);
        AutomationProperties.SetName(button, strings.Translate);
        return new TranslationActionVisual(button, label, loading);
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
