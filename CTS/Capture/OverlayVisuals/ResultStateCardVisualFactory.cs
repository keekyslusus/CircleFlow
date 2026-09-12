namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal sealed record ResultStateCardAction(
    string Label,
    Action Execute);

internal sealed record ResultStateCardOptions(
    Geometry Icon,
    string Message,
    string AccessibleName,
    string CloseLabel,
    Action Close,
    ResultStateCardAction? PrimaryAction = null);

internal static class ResultStateCardVisualFactory
{
    internal static ResultStateCardVisual Create(
        ResultStateCardOptions options,
        ResultStateCardPalette palette)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(options.Icon);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Message);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccessibleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CloseLabel);
        ArgumentNullException.ThrowIfNull(options.Close);
        if (options.PrimaryAction is { } action)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action.Label);
            ArgumentNullException.ThrowIfNull(action.Execute);
        }

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var close = OverlayVisualResources.IconButton(
            OverlayVisualResources.CloseIconGeometry,
            options.CloseLabel,
            palette.MutedText,
            palette.SecondaryContainer,
            palette.OnSecondaryContainer,
            iconSize: 12);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, -4, -6, 0);
        close.Click += (_, _) => options.Close();
        Panel.SetZIndex(close, 1);
        root.Children.Add(close);

        var icon = OverlayVisualResources.Icon(options.Icon, 22, palette.OnSecondaryContainer);
        var message = new TextBlock
        {
            Text = options.Message,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            Width = 230,
            Margin = new Thickness(12, 2, 26, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = OverlayVisualResources.Frozen(palette.SecondaryContainer),
            Child = icon,
        });
        row.Children.Add(message);
        Grid.SetRow(row, 0);
        root.Children.Add(row);

        Button? primaryActionButton = null;
        if (options.PrimaryAction is { } primaryAction)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            primaryActionButton = TextPillButton(primaryAction.Label, palette);
            primaryActionButton.HorizontalAlignment = HorizontalAlignment.Right;
            primaryActionButton.Margin = new Thickness(0, 10, 0, 0);
            primaryActionButton.Click += (_, _) => primaryAction.Execute();
            Grid.SetRow(primaryActionButton, 1);
            root.Children.Add(primaryActionButton);
        }

        var card = new Border
        {
            Width = 340,
            MaxWidth = 540,
            Child = root,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 12, 14, 12),
            Effect = OverlayVisualResources.DockShadow(10, palette.ShadowOpacity),
        };
        AutomationProperties.SetName(card, options.AccessibleName);
        return new ResultStateCardVisual(card, icon, message, close, primaryActionButton);
    }

    private static Button TextPillButton(string label, ResultStateCardPalette palette)
    {
        var button = new Button
        {
            Content = label,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            FontFamily = OverlayVisualResources.Font,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Foreground = OverlayVisualResources.Frozen(palette.OnPrimaryContainer),
            Background = OverlayVisualResources.Frozen(palette.PrimaryContainer),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        OverlayVisualResources.ApplyButtonTemplate(
            button, 17, palette.SecondaryContainer, palette.OnSecondaryContainer);
        AutomationProperties.SetName(button, label);
        return button;
    }
}
