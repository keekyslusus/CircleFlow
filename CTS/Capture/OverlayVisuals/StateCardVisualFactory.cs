namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal sealed record StateCardAction(
    string Label,
    Action Execute);

internal sealed record StateCardOptions(
    Geometry Icon,
    string Message,
    string AccessibleName,
    string CloseLabel,
    Action Close,
    StateCardAction? PrimaryAction = null,
    string? Title = null,
    double? CardWidth = null,
    double? CardMaxHeight = null);

internal static class StateCardVisualFactory
{
    internal static StateCardVisual Create(
        StateCardOptions options,
        CardPalette palette)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Message);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccessibleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CloseLabel);
        if (options.Title is not null) ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        if (options.CardWidth is { } width && (!double.IsFinite(width) || width < 160))
            throw new ArgumentOutOfRangeException(nameof(options.CardWidth));
        if (options.CardMaxHeight is { } maxHeight && (!double.IsFinite(maxHeight) || maxHeight < 112))
            throw new ArgumentOutOfRangeException(nameof(options.CardMaxHeight));
        if (options.PrimaryAction is { } action)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action.Label);
        }

        var cardWidth = options.CardWidth ?? 340;
        var textWidth = cardWidth - 110;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var close = OverlayVisualResources.IconButton(
            PluginIcons.CloseFilled,
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
            Width = textWidth,
            Margin = new Thickness(12, 2, 26, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var iconContainer = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = OverlayVisualResources.Frozen(palette.SecondaryContainer),
            Child = icon,
        };
        if (options.Title is not null) iconContainer.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(iconContainer);
        TextBlock? title = null;
        if (options.Title is { } titleText)
        {
            title = new TextBlock
            {
                Text = titleText,
                FontFamily = OverlayVisualResources.Font,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = OverlayVisualResources.Frozen(palette.Text),
                TextWrapping = TextWrapping.Wrap,
                Width = textWidth,
            };
            message.Margin = new Thickness(0, 6, 0, 0);
            message.VerticalAlignment = VerticalAlignment.Top;
            var column = new StackPanel { Margin = new Thickness(12, 2, 26, 0) };
            column.Children.Add(title);
            column.Children.Add(message);
            if (options.CardMaxHeight is { } cardMaxHeight)
            {
                title.Width = textWidth - 18;
                message.Width = textWidth - 18;
                column.Margin = new Thickness();
                column.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(new ScrollViewer
                {
                    Content = column,
                    Width = textWidth,
                    MaxHeight = Math.Max(42, cardMaxHeight - 26 -
                        (options.PrimaryAction is null ? 0 : 44)),
                    Margin = new Thickness(12, 2, 26, 0),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });
            }
            else row.Children.Add(column);
        }
        else row.Children.Add(message);
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
            Width = cardWidth,
            MaxWidth = 540,
            MaxHeight = options.CardMaxHeight ?? double.PositiveInfinity,
            Child = root,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = PluginShapes.LargeCorners,
            Padding = new Thickness(14, 12, 14, 12),
            Effect = OverlayVisualResources.DockShadow(10, palette.ShadowOpacity),
        };
        AutomationProperties.SetName(card, options.AccessibleName);
        return new StateCardVisual(card, icon, message, close, primaryActionButton, title);
    }

    private static Button TextPillButton(string label, CardPalette palette)
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
            button, button.Height / 2, palette.SecondaryContainer, palette.OnSecondaryContainer);
        AutomationProperties.SetName(button, label);
        return button;
    }
}
