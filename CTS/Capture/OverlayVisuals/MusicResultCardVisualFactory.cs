using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal static class MusicResultCardVisualFactory
{
    internal static Border Create(
        ShazamRecognition recognition,
        bool lightTheme,
        UiStrings strings,
        Action<IOverlayCommand> publish,
        Action<string, Button> copy,
        double availableWidth)
    {
        var theme = PluginPalette.For(lightTheme);
        var palette = theme.Card;
        var content = new Grid();
        var card = new Border
        {
            Child = content,
            MinWidth = Math.Min(360, availableWidth),
            MaxWidth = Math.Min(640, availableWidth),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = OverlayVisualResources.Frozen(theme.SelectionChip.Divider),
            BorderThickness = new Thickness(1),
            CornerRadius = PluginShapes.ExtraLargeCorners,
            Effect = OverlayVisualResources.DockShadow(10, palette.ShadowOpacity),
        };
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, OverlayVisualResources.Frozen(palette.Surface)));
        var hover = new System.Windows.Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, OverlayVisualResources.Frozen(PluginPalette.TraceCardHover(lightTheme))));
        style.Triggers.Add(hover);
        card.Style = style;
        AutomationProperties.SetName(card, strings.MusicResultTitle);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(CreateCover(recognition.CoverUrl, palette));
        var info = new StackPanel
        {
            Margin = new Thickness(14, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = Text(recognition.Title, 17, palette.Text);
        title.FontWeight = FontWeights.SemiBold;
        // Reserve the corner actions even when a short title determines the card width.
        title.Margin = new Thickness(0, 0, 52, 0);
        info.Children.Add(title);
        var artist = Text(recognition.Artist, 13, palette.Text);
        artist.Margin = new Thickness(0, 4, 0, 0);
        info.Children.Add(artist);
        if (!string.IsNullOrWhiteSpace(recognition.Album))
        {
            var album = Text(recognition.Album, 12, palette.MutedText);
            album.Margin = new Thickness(0, 9, 0, 0);
            info.Children.Add(album);
        }
        Grid.SetColumn(info, 1);
        row.Children.Add(info);

        if (Search.MusicResultPresenter.IsSafeShazamUrl(recognition.ShazamUrl))
        {
            var open = new Button
            {
                Content = row,
                Padding = new Thickness(10),
                BorderThickness = new Thickness(0),
                Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
                Foreground = OverlayVisualResources.Frozen(palette.Primary),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                ToolTip = strings.OpenInShazam,
            };
            OverlayVisualResources.ApplyButtonTemplate(open, PluginShapes.ExtraLarge, PluginPalette.Transparent, palette.Primary);
            AutomationProperties.SetName(open, strings.OpenInShazam);
            open.Click += (_, e) => { e.Handled = true; publish(new OpenMusicResult()); };
            content.Children.Add(open);
        }
        else
        {
            row.Margin = new Thickness(10);
            content.Children.Add(row);
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
        };
        var copyButton = OverlayVisualResources.IconButton(
            PluginIcons.CopyFilled, strings.CopyTrackInfo,
            palette.MutedText, palette.SecondaryContainer, palette.OnSecondaryContainer, 12);
        copyButton.Click += (_, e) =>
        {
            e.Handled = true;
            copy(recognition.TrackInfo, copyButton);
        };
        actions.Children.Add(copyButton);
        var close = OverlayVisualResources.IconButton(
            PluginIcons.CloseFilled, strings.Close,
            palette.MutedText, palette.SecondaryContainer, palette.OnSecondaryContainer, 12);
        close.Click += (_, e) => { e.Handled = true; publish(new DismissMusicResult()); };
        actions.Children.Add(close);
        content.Children.Add(actions);
        return card;
    }

    private static TextBlock Text(string value, double size, Color color) => new()
    {
        Text = value,
        ToolTip = value,
        FontFamily = OverlayVisualResources.Font,
        FontSize = size,
        Foreground = OverlayVisualResources.Frozen(color),
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static FrameworkElement CreateCover(string? url, CardPalette palette)
    {
        var cover = new Grid
        {
            Width = 98,
            Height = 98,
            Background = OverlayVisualResources.Frozen(palette.SecondaryContainer),
            Clip = new RectangleGeometry(new Rect(0, 0, 98, 98), PluginShapes.Large, PluginShapes.Large),
        };
        cover.Children.Add(OverlayVisualResources.Icon(PluginIcons.MusicFilled, 30, palette.Primary));
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.DecodePixelWidth = 196;
                bitmap.EndInit();
                cover.Children.Add(OverlayVisualResources.FadeInImage(bitmap));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.IO.IOException or NotSupportedException)
            {
                // Missing or unreadable artwork leaves the music placeholder visible.
            }
        }
        return cover;
    }
}
