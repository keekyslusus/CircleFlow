using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal sealed class TraceOverlayVisual : IOverlayWidgetVisual
{
    private readonly OverlayWidgetCardHost _host;
    private readonly UiStrings _strings;
    private readonly bool _light;
    private readonly Action<Uri> _open;
    private readonly Action _close;
    private readonly ClipboardCopyService _clipboardCopy;
    private readonly Func<Uri, ITraceVideoPreview>? _createVideo;
    private ITraceVideoPreview? _media;
    private Border? _card;

    private TraceOverlayVisual(OverlayWidgetContext context, Func<Uri, ITraceVideoPreview>? createVideo)
    {
        _host = new OverlayWidgetCardHost(context);
        _strings = context.Strings;
        _light = context.LightTheme;
        _open = context.Open;
        _close = context.Close;
        _clipboardCopy = context.ClipboardCopy;
        _createVideo = createVideo;
    }

    internal Task Presentation => _host.Presentation;

    internal static TraceOverlayVisual Create(OverlayWidgetContext context, Func<Uri, ITraceVideoPreview>? createVideo = null)
    {
        var visual = new TraceOverlayVisual(context, createVideo);
        visual._host.ShowLoading(context.Strings.TraceSearching, PluginPalette.For(context.LightTheme).Roles.Primary);
        return visual;
    }

    public void ShowResult(VisualSearchPreparationOutcome outcome)
    {
        if (!_host.IsActive || _host.HasCard) return;
        var theme = PluginPalette.For(_light);
        var palette = theme.Card;
        var match = outcome.PreparedSearch?.TraceMatch;

        if (match is not null)
        {
            var content = new Grid();
            _card = new Border
            {
                Child = content, Width = _host.CardWidth,
                Background = OverlayVisualResources.Frozen(palette.Surface),
                BorderBrush = OverlayVisualResources.Frozen(theme.SelectionChip.Divider),
                BorderThickness = new Thickness(1), CornerRadius = PluginShapes.ExtraLargeCorners,
                Padding = new Thickness(0), Effect = OverlayVisualResources.DockShadow(10, palette.ShadowOpacity),
            };
            _card.ClearValue(Border.BackgroundProperty);
            var cardStyle = new Style(typeof(Border));
            cardStyle.Setters.Add(new Setter(Border.BackgroundProperty, OverlayVisualResources.Frozen(palette.Surface)));
            var hover = new System.Windows.Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, OverlayVisualResources.Frozen(PluginPalette.TraceCardHover(_light))));
            cardStyle.Triggers.Add(hover);
            _card.Style = cardStyle;
            var compact = _card.Width < 480;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 0 : 172) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var media = CreateMedia(match, 172);
            if (compact) Grid.SetColumn(media, 1);
            media.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(media);
            var info = new StackPanel { Margin = compact ? new Thickness(0, 14, 0, 0) : new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(info, 1);
            if (compact) Grid.SetRow(info, 1);
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, compact ? 0 : 56, 0) };
            var percentText = Text(match.Similarity.ToString("P1", CultureInfo.CurrentCulture), 11);
            percentText.FontWeight = FontWeights.SemiBold;
            percentText.Foreground = OverlayVisualResources.Frozen(palette.OnSecondaryContainer);
            percentText.LineHeight = 16.5;
            percentText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            var percent = new Border
            {
                Child = percentText, Background = OverlayVisualResources.Frozen(palette.SecondaryContainer),
                Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            // WPF does not clamp oversized radii to semicircular ends like CSS does.
            percent.SizeChanged += (_, _) => percent.CornerRadius = new CornerRadius(percent.ActualHeight / 2);
            DockPanel.SetDock(percent, Dock.Left);
            titleRow.Children.Add(percent);
            var title = Text(match.Title, 17);
            title.FontWeight = FontWeights.SemiBold;
            title.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(title);
            info.Children.Add(titleRow);
            var native = Text(match.NativeTitle, 12, muted: true);
            native.Margin = new Thickness(0, 3, 0, 0);
            info.Children.Add(native);
            var episode = string.IsNullOrEmpty(match.Episode) ? "" : string.Format(CultureInfo.CurrentCulture, _strings.TraceEpisode, match.Episode);
            var metadata = Text(string.Join(" · ", new[] { episode, match.Format, match.Year, match.Studio }.Where(s => !string.IsNullOrEmpty(s))), 12, muted: true);
            metadata.Margin = new Thickness(0, 4, 0, 0);
            info.Children.Add(metadata);
            info.Children.Add(CreateTimeline(match));
            row.Children.Add(info);
            row.IsHitTestVisible = false;
            var openButton = new Button
            {
                Content = row, Padding = new Thickness(10), BorderThickness = new Thickness(0),
                Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
                Foreground = OverlayVisualResources.Frozen(palette.Primary),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand, ToolTip = _strings.TraceOpen,
            };
            OverlayVisualResources.ApplyButtonTemplate(openButton, PluginShapes.ExtraLarge,
                PluginPalette.Transparent, palette.Primary);
            AutomationProperties.SetName(openButton, $"{_strings.TraceOpen}: {match.Title}");
            openButton.Click += (_, e) => { if (_host.IsActive) _open(new Uri(match.AnilistUrl)); e.Handled = true; };
            content.Children.Add(openButton);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) };
            var copy = OverlayVisualResources.IconButton(
                PluginIcons.CopyFilled,
                _strings.TraceCopy,
                palette.MutedText,
                palette.SecondaryContainer,
                palette.OnSecondaryContainer,
                12);
            var copyIcon = copy.Content;
            copy.Click += (_, e) =>
            {
                e.Handled = true;
                var payload = $"{match.Title} - {string.Format(CultureInfo.CurrentCulture, _strings.TraceEpisode, match.Episode)}, {TraceMoeMatch.Timestamp(match.From)}";
                if (_clipboardCopy.TryCopy(payload))
                {
                    copy.ToolTip = _strings.Copied;
                    AutomationProperties.SetName(copy, _strings.Copied);
                    copy.Content = OverlayVisualResources.Icon(PluginIcons.CheckFilled, 12, palette.Primary);
                }
                else
                {
                    copy.ToolTip = _strings.CopyFailed;
                    AutomationProperties.SetName(copy, _strings.CopyFailed);
                    copy.Content = copyIcon;
                }
            };
            actions.Children.Add(copy);
            var close = OverlayVisualResources.IconButton(
                PluginIcons.CloseFilled,
                _strings.Close,
                palette.MutedText,
                palette.SecondaryContainer,
                palette.OnSecondaryContainer,
                12);
            close.Click += (_, e) =>
            {
                e.Handled = true;
                CloseResult();
            };
            actions.Children.Add(close);
            content.Children.Add(actions);
        }
        else
        {
            var message = outcome.Failure switch
            {
                UploadFailure.None => _strings.TraceNoMatch,
                UploadFailure.Timeout => _strings.SearchTimedOut,
                UploadFailure.BadResponse => _strings.SearchUnexpectedResponse,
                UploadFailure.UnexpectedStatus when outcome.StatusCode == 429 => _strings.TraceRateLimited,
                UploadFailure.UnexpectedStatus => _strings.SearchUnexpectedStatus(outcome.StatusCode),
                _ => _strings.SearchNetworkError,
            };
            var options = new StateCardOptions(
                PluginIcons.TraceMoe,
                message,
                _strings.TraceMoeProviderName,
                _strings.Close,
                CloseResult);
            _card = StateCardVisualFactory.Create(options, theme.Card).Card;
        }
        _host.Present(_card, match is not null, _media is null ? null : WaitForMediaAsync);
    }

    private void CloseResult()
    {
        if (!_host.IsActive) return;
        DismissResult();
        _close();
    }

    private async Task WaitForMediaAsync(CancellationToken cancellation)
    {
        bool ready;
        try { ready = await _media!.Ready.WaitAsync(TimeSpan.FromSeconds(8), cancellation); }
        catch (TimeoutException) { ready = false; }
        if (!ready) _media!.Dispose();
    }

    private FrameworkElement CreateMedia(TraceMoeMatch match, double width)
    {
        var grid = new Grid { Width = width, Height = 97, Background = OverlayVisualResources.Frozen(PluginPalette.OpaqueBlack),
            Clip = new RectangleGeometry(new Rect(0, 0, width, 97), PluginShapes.Large, PluginShapes.Large) };
        if (match.Image is not null)
            grid.Children.Add(new Image { Source = new BitmapImage(match.Image), Stretch = Stretch.UniformToFill });
        if (match.Video is not null && _createVideo is not null)
        {
            _media = _createVideo(match.Video);
            grid.Children.Add(_media.Root);
        }
        return grid;
    }

    private FrameworkElement CreateTimeline(TraceMoeMatch match)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        const double trackHeight = 5, segmentHeight = 15, cutout = 3;
        var track = new Grid { Height = trackHeight };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(trackHeight / 2),
            Background = OverlayVisualResources.Frozen(PluginPalette.TraceTimelineTrack(_light)) });
        var segment = new Border { Height = segmentHeight, CornerRadius = new CornerRadius(segmentHeight / 2), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(cutout),
            Child = new Border { CornerRadius = new CornerRadius(segmentHeight / 2 - cutout),
                Background = OverlayVisualResources.Frozen(PluginPalette.For(_light).Card.Primary) } };
        // The cutout must follow the actual card surface, including its hover state.
        segment.SetBinding(Border.BackgroundProperty, new Binding(nameof(Border.Background)) { Source = _card });
        track.Children.Add(segment);
        track.SizeChanged += (_, _) =>
        {
            var duration = Math.Max(1, match.Duration);
            var width = Math.Min(track.ActualWidth, Math.Max(15, track.ActualWidth * Math.Max(0, match.To - match.From) / duration));
            segment.Width = width + 2 * cutout;
            var overhang = (segmentHeight - trackHeight) / 2;
            segment.Margin = new Thickness(Math.Clamp(track.ActualWidth * match.From / duration, 0, Math.Max(0, track.ActualWidth - width)) - cutout, -overhang, -cutout, -overhang);
        };
        panel.Children.Add(track);
        var scale = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        scale.Children.Add(Text(TraceMoeMatch.Timestamp(0), 11, muted: true));
        var end = Text(TraceMoeMatch.Timestamp(match.Duration), 11, muted: true);
        end.HorizontalAlignment = HorizontalAlignment.Right;
        scale.Children.Add(end);
        panel.Children.Add(scale);
        return panel;
    }

    private TextBlock Text(string value, double size, bool muted = false, bool primary = false)
    {
        var palette = PluginPalette.For(_light).Card;
        return new TextBlock { Text = value, FontFamily = OverlayVisualResources.Font, FontSize = size,
            Foreground = OverlayVisualResources.Frozen(primary ? palette.Primary : muted ? palette.MutedText : palette.Text),
            TextTrimming = TextTrimming.CharacterEllipsis };
    }

    public void DismissResult()
    {
        if (!_host.IsActive) return;
        _media?.Dispose();
        _host.Dismiss();
    }

    public void Dispose()
    {
        _host.Dispose();
        _media?.Dispose();
    }
}
