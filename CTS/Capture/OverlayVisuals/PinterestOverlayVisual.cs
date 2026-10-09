using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Emoji;

namespace CircleToSearch.Capture;

internal sealed class PinterestOverlayVisual : IOverlayWidgetVisual
{
    internal const double StripHeight = 132;
    internal const double MasonryMaxHeight = 340;
    private const double TileGap = 8;
    private const double MinTileWidth = 96;
    private const double MaxTileWidth = 220;
    // Twice the widest tile keeps previews sharp on high-DPI screens; Pinterest's previews are 474 px wide anyway.
    private const int PreviewDecodeWidth = (int)(MaxTileWidth * 2);
    private const double MoreTileMinWidth = 72;
    private const double MasonryColumnMinWidth = 140;
    private const double MasonryFadeHeight = 48;
    private static readonly TimeSpan PreviewWait = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan HoverIn = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HoverOut = TimeSpan.FromMilliseconds(140);
    private const double HoverTextRise = 6;
    private static readonly TimeSpan FadeMotion = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan FullImageWait = TimeSpan.FromSeconds(20);

    private readonly OverlayWidgetCardHost _host;
    private readonly Grid _root;
    private readonly UiStrings _strings;
    private readonly bool _light;
    private readonly Action<Uri> _open;
    private readonly Action _close;
    private readonly ClipboardCopyService _clipboardCopy;
    private readonly Action<BitmapSource>? _saveImage;
    private readonly Action<ToastNotification> _showToast;
    private readonly EmojiText? _emoji;
    private readonly List<BitmapImage> _stripImages = [];
    private readonly Dictionary<string, Task<BitmapSource?>> _fullImages = [];
    private IReadOnlyList<PinterestPin> _pins = [];
    private IReadOnlyDictionary<string, BitmapImage> _previews = new Dictionary<string, BitmapImage>();
    private Border? _body;
    private Button? _back;
    private FrameworkElement? _strip;
    private ScrollViewer? _masonry;
    private AutoHideScrollbarController? _scrollbar;
    private SmoothScrollMotionController? _scrollMotion;
    private OverlayContextMenu? _menu;
    private double _contentWidth;

    private PinterestOverlayVisual(OverlayWidgetContext context, EmojiText? emoji)
    {
        _emoji = emoji;
        _host = new OverlayWidgetCardHost(context);
        _root = context.Root;
        _strings = context.Strings;
        _light = context.LightTheme;
        _open = context.Open;
        _close = context.Close;
        _clipboardCopy = context.ClipboardCopy;
        _saveImage = context.SaveImage;
        _showToast = context.ShowToast ?? (_ => { });
    }

    internal Task Presentation => _host.Presentation;

    internal bool IsExpanded { get; private set; }

    internal bool IsMenuOpen => _menu?.IsOpen == true;

    internal static PinterestOverlayVisual Create(OverlayWidgetContext context, EmojiText? emoji = null)
    {
        emoji?.Preload();
        var visual = new PinterestOverlayVisual(context, emoji);
        visual._host.ShowLoading(context.Strings.PinterestSearching, PluginPalette.For(context.LightTheme).Roles.Primary);
        return visual;
    }

    public void ShowResult(VisualSearchPreparationOutcome outcome)
    {
        if (!_host.IsActive || _host.HasCard) return;
        var theme = PluginPalette.For(_light);
        _pins = outcome.PreparedSearch?.PinterestPins ?? [];
        if (_pins.Count == 0)
        {
            var message = outcome.Failure switch
            {
                UploadFailure.None => _strings.PinterestNoMatch,
                UploadFailure.Timeout => _strings.SearchTimedOut,
                UploadFailure.BadResponse => _strings.SearchUnexpectedResponse,
                UploadFailure.UnexpectedStatus => _strings.SearchUnexpectedStatus(outcome.StatusCode),
                _ => _strings.SearchNetworkError,
            };
            var options = new StateCardOptions(
                PluginIcons.PinterestMark,
                message,
                _strings.PinterestProviderName,
                _strings.Close,
                CloseResult);
            _host.Present(StateCardVisualFactory.Create(options, theme.Card).Card, matched: false);
            return;
        }

        var palette = theme.Card;
        var width = _host.CardWidth;
        const double padding = 16;
        _contentWidth = width - 2 - padding * 2;
        // Every preview starts loading now, so the grid behind '+N' is mostly ready before it is opened.
        _previews = _pins.ToDictionary(pin => pin.Id, pin => LoadPreview(pin.Image));
        _menu = new OverlayContextMenu(_root, _light);
        var content = new StackPanel();
        content.Children.Add(CreateHeader());
        _strip = CreateStrip();
        _body = new Border { Margin = new Thickness(0, 12, 0, 0), Child = _strip };
        content.Children.Add(_body);
        var card = new Border
        {
            Child = content, Width = width,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(theme.SelectionChip.Divider),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24),
            Padding = new Thickness(padding), Effect = OverlayVisualResources.DockShadow(10, palette.ShadowOpacity),
        };
        AutomationProperties.SetName(card, _strings.PinterestProviderName);
        _host.Present(card, matched: true, WaitForStripPreviewsAsync);
    }

    private FrameworkElement CreateHeader()
    {
        var palette = PluginPalette.For(_light).Card;
        var header = new DockPanel();
        var close = OverlayVisualResources.IconButton(
            PluginIcons.CloseFilled,
            _strings.Close,
            palette.MutedText,
            palette.SecondaryContainer,
            palette.OnSecondaryContainer,
            12);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, e) =>
        {
            e.Handled = true;
            CloseResult();
        };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        _back = OverlayVisualResources.IconButton(
            PluginIcons.ArrowBackFilled,
            _strings.PinterestBack,
            palette.MutedText,
            palette.SecondaryContainer,
            palette.OnSecondaryContainer,
            12);
        _back.VerticalAlignment = VerticalAlignment.Top;
        _back.Visibility = Visibility.Collapsed;
        _back.Click += (_, e) =>
        {
            e.Handled = true;
            Collapse();
        };
        DockPanel.SetDock(_back, Dock.Right);
        header.Children.Add(_back);
        var mark = OverlayVisualResources.BrandMark(24,
            (OverlayVisualResources.Frozen(PluginPalette.PinterestRed), PluginIcons.PinterestMark));
        mark.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(mark, Dock.Left);
        header.Children.Add(mark);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(_strings.PinterestProviderName, PluginTypography.Subtitle, palette.Text);
        title.FontWeight = FontWeights.SemiBold;
        titles.Children.Add(title);
        titles.Children.Add(Text(_strings.PinterestSummary(_pins.Count), PluginTypography.Caption, palette.MutedText));
        header.Children.Add(titles);
        return header;
    }

    // Fills one row with as many whole previews as fit; the rest collapse into a tile that expands the grid.
    private FrameworkElement CreateStrip()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = StripHeight };
        var used = 0.0;
        var shown = 0;
        foreach (var pin in _pins)
        {
            var gap = shown == 0 ? 0 : TileGap;
            var width = Math.Clamp(StripHeight * AspectRatio(pin), MinTileWidth, MaxTileWidth);
            var reserve = shown + 1 < _pins.Count ? TileGap + MoreTileMinWidth : 0;
            if (used + gap + width + reserve > _contentWidth) break;
            row.Children.Add(CreatePinTile(pin, width, StripHeight, new Thickness(gap, 0, 0, 0), 14, _stripImages));
            used += gap + width;
            shown++;
        }
        if (shown < _pins.Count)
        {
            var gap = shown == 0 ? 0 : TileGap;
            row.Children.Add(CreateMoreTile(_pins[shown], Math.Max(MoreTileMinWidth, _contentWidth - used - gap), gap,
                _pins.Count - shown));
        }
        return row;
    }

    private ScrollViewer CreateMasonry()
    {
        var columns = Math.Clamp((int)((_contentWidth + TileGap) / (MasonryColumnMinWidth + TileGap)), 2, 4);
        var columnWidth = (_contentWidth - TileGap * (columns - 1)) / columns;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var stacks = new StackPanel[columns];
        var heights = new double[columns];
        for (var index = 0; index < columns; index++)
        {
            stacks[index] = new StackPanel { Width = columnWidth, Margin = new Thickness(index == 0 ? 0 : TileGap, 0, 0, 0) };
            row.Children.Add(stacks[index]);
        }
        foreach (var pin in _pins)
        {
            var column = Array.IndexOf(heights, heights.Min());
            var height = Math.Clamp(columnWidth / AspectRatio(pin), columnWidth / 2, columnWidth * 2);
            var top = stacks[column].Children.Count == 0 ? 0 : TileGap;
            stacks[column].Children.Add(CreatePinTile(pin, columnWidth, height, new Thickness(0, top, 0, 0), 12, null));
            heights[column] += top + height;
        }
        var translation = new TranslateTransform();
        row.RenderTransform = translation;
        var scroll = new ScrollViewer
        {
            Content = row, MaxHeight = MasonryMaxHeight, Focusable = false, PanningMode = PanningMode.VerticalOnly,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        OverlayVisualResources.ApplyAutoHideScrollbar(scroll, PluginPalette.For(_light).SearchBrowserScrollbarThumb);
        _scrollbar = new AutoHideScrollbarController(scroll);
        _scrollMotion = new SmoothScrollMotionController(scroll, translation);
        // The fade hints that more pins are below; its edge slides away once the end is reached.
        var edge = new GradientStop(PluginPalette.OpaqueBlack, 1);
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(edge);
        fade.GradientStops.Add(new GradientStop(PluginPalette.Transparent, 1));
        scroll.OpacityMask = fade;
        bool? fadeShown = null;
        scroll.ScrollChanged += (_, _) =>
        {
            var show = scroll.VerticalOffset < scroll.ScrollableHeight - 1;
            if (show == fadeShown) return;
            fadeShown = show;
            Animate(edge, GradientStop.OffsetProperty, show ? 1 - MasonryFadeHeight / MasonryMaxHeight : 1, FadeMotion);
        };
        return scroll;
    }

    private Button CreatePinTile(PinterestPin pin, double width, double height, Thickness margin, double radius,
        List<BitmapImage>? previews)
    {
        var palette = PluginPalette.For(_light).Card;
        var image = _previews[pin.Id];
        previews?.Add(image);
        var surface = new Grid
        {
            Width = width, Height = height,
            Background = OverlayVisualResources.Frozen(palette.SecondaryContainer),
            Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius),
        };
        surface.Children.Add(OverlayVisualResources.FadeInImage(image));
        var details = CreateHoverDetails(pin, width);
        if (details is not null) surface.Children.Add(details);
        var outline = new Border
        {
            CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(2),
            BorderBrush = OverlayVisualResources.Frozen(palette.Primary), Opacity = 0, IsHitTestVisible = false,
        };
        surface.Children.Add(outline);
        var name = pin.Title.Length > 0 ? $"{_strings.PinterestOpen}: {pin.Title}" : _strings.PinterestOpen;
        // No tooltip: the hover caption already shows the title and a tooltip would cover the next tile.
        var button = TileButton(surface, margin, radius, name);
        void ShowHover(bool visible)
        {
            var duration = visible ? HoverIn : HoverOut;
            Animate(outline, UIElement.OpacityProperty, visible ? 1 : 0, duration);
            if (details is null) return;
            Animate(details, UIElement.OpacityProperty, visible ? 1 : 0, duration);
            Animate(details.Children[0].RenderTransform, TranslateTransform.YProperty, visible ? 0 : HoverTextRise, duration);
        }
        button.MouseEnter += (_, _) => ShowHover(true);
        button.MouseLeave += (_, _) => ShowHover(false);
        button.GotKeyboardFocus += (_, _) => ShowHover(true);
        button.LostKeyboardFocus += (_, _) => ShowHover(button.IsMouseOver);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            if (_host.IsActive) _open(new Uri(pin.PinUrl));
        };
        button.ContextMenuOpening += (_, e) =>
        {
            e.Handled = true;
            // The menu keys report no cursor; the menu then opens from the middle of the tile.
            var keyboard = e.CursorLeft < 0 && e.CursorTop < 0;
            OpenPinMenu(pin, button,
                keyboard ? new Point(button.ActualWidth / 2, button.ActualHeight / 2) : Mouse.GetPosition(button), keyboard);
        };
        return button;
    }

    private void OpenPinMenu(PinterestPin pin, Button tile, Point position, bool keyboard)
    {
        if (!_host.IsActive || _menu is null) return;
        // The full image starts loading with the menu, so it is usually ready by the time an item is chosen.
        _ = FullImageAsync(pin);
        var items = new List<OverlayContextMenuItem>
        {
            new(PluginIcons.CopyOutlined, _strings.PinterestCopyImage,
                () => _ = WithFullImageAsync(pin, image => _clipboardCopy.TryCopyImage(image))),
        };
        if (_saveImage is { } save)
            items.Add(new(PluginIcons.DownloadOutlined, _strings.PinterestSaveImage,
                () => _ = WithFullImageAsync(pin, save)));
        _menu.Open(tile, position, items, focusFirst: keyboard);
    }

    private async Task WithFullImageAsync(PinterestPin pin, Action<BitmapSource> use)
    {
        var image = await FullImageAsync(pin);
        if (!_host.IsActive) return;
        if (image is not null)
        {
            use(image);
            return;
        }
        _fullImages.Remove(pin.Id);
        _showToast(new ToastNotification(_strings.PinterestImageUnavailable, ToastTone.Error));
    }

    private Task<BitmapSource?> FullImageAsync(PinterestPin pin)
    {
        if (!_fullImages.TryGetValue(pin.Id, out var image))
            _fullImages[pin.Id] = image = LoadFullImageAsync(pin.FullImage);
        return image;
    }

    private Grid? CreateHoverDetails(PinterestPin pin, double width)
    {
        if (pin.Title.Length == 0 && pin.Domain.Length == 0) return null;
        var scrim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        scrim.GradientStops.Add(new GradientStop(PluginPalette.Transparent, 0.35));
        scrim.GradientStops.Add(new GradientStop(PluginPalette.PinterestTileScrim, 1));
        scrim.Freeze();
        var details = new Grid { Background = scrim, Opacity = 0, IsHitTestVisible = false };
        var texts = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 10),
            RenderTransform = new TranslateTransform(0, HoverTextRise),
        };
        if (pin.Title.Length > 0)
        {
            var title = Text(pin.Title, PluginTypography.Caption, PluginPalette.PinterestTileText);
            title.FontWeight = FontWeights.SemiBold;
            title.TextWrapping = TextWrapping.Wrap;
            title.MaxHeight = 32;
            title.MaxWidth = width - 20;
            _emoji?.SetText(title, pin.Title);
            texts.Children.Add(title);
        }
        if (pin.Domain.Length > 0) texts.Children.Add(Text(pin.Domain, PluginTypography.Caption, PluginPalette.PinterestTileMutedText));
        details.Children.Add(texts);
        return details;
    }

    private Button CreateMoreTile(PinterestPin preview, double width, double gap, int remaining)
    {
        var surface = new Grid
        {
            Width = width, Height = StripHeight,
            Background = OverlayVisualResources.Frozen(PluginPalette.For(_light).Card.SecondaryContainer),
            Clip = new RectangleGeometry(new Rect(0, 0, width, StripHeight), 14, 14),
        };
        var image = _previews[preview.Id];
        _stripImages.Add(image);
        surface.Children.Add(OverlayVisualResources.FadeInImage(image));
        surface.Children.Add(new Border { Background = OverlayVisualResources.Frozen(PluginPalette.PinterestMoreScrim) });
        var count = Text(_strings.PinterestMore(remaining), PluginTypography.Title, PluginPalette.PinterestTileText);
        count.FontWeight = FontWeights.SemiBold;
        count.HorizontalAlignment = HorizontalAlignment.Center;
        count.VerticalAlignment = VerticalAlignment.Center;
        surface.Children.Add(count);
        var button = TileButton(surface, new Thickness(gap, 0, 0, 0), 14, _strings.PinterestShowAll);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            Expand();
        };
        return button;
    }

    private Button TileButton(FrameworkElement content, Thickness margin, double radius, string name)
    {
        var button = new Button
        {
            Content = content, Margin = margin, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand,
        };
        OverlayVisualResources.ApplyButtonTemplate(button, radius, PluginPalette.Transparent,
            PluginPalette.For(_light).Card.Primary);
        AutomationProperties.SetName(button, name);
        return button;
    }

    internal void Expand()
    {
        if (!_host.IsActive || _body is null || IsExpanded) return;
        IsExpanded = true;
        _menu?.Close();
        _masonry ??= CreateMasonry();
        _host.ChangeLayout(() =>
        {
            _body.Child = _masonry;
            _back!.Visibility = Visibility.Visible;
        });
    }

    internal void Collapse()
    {
        if (!_host.IsActive || _body is null || !IsExpanded) return;
        IsExpanded = false;
        _menu?.Close();
        _scrollMotion?.Reset();
        _host.ChangeLayout(() =>
        {
            _body.Child = _strip;
            _back!.Visibility = Visibility.Collapsed;
        });
    }

    public bool TryCopy() => false;

    public bool TryGoBack()
    {
        if (!_host.IsActive) return false;
        if (IsMenuOpen)
        {
            _menu!.Close();
            return true;
        }
        if (!IsExpanded) return false;
        Collapse();
        return true;
    }

    // Hover motion starts from the value on screen, so moving quickly across tiles never jumps.
    private static void Animate(DependencyObject target, DependencyProperty property, double value, TimeSpan duration)
    {
        if (OverlayVisualResources.AnimationsEnabled())
        {
            MotionValue.Animate(target, property, value, duration, new CubicEase { EasingMode = EasingMode.EaseOut });
            return;
        }
        (target as IAnimatable)?.BeginAnimation(property, null);
        target.SetValue(property, value);
    }

    private async Task WaitForStripPreviewsAsync(CancellationToken cancellation)
    {
        try { await Task.WhenAll(_stripImages.Select(Loaded)).WaitAsync(PreviewWait, cancellation); }
        catch (TimeoutException) { }
    }

    private static Task Loaded(BitmapImage image)
    {
        if (!image.IsDownloading) return Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        image.DownloadCompleted += (_, _) => done.TrySetResult();
        image.DownloadFailed += (_, _) => done.TrySetResult();
        image.DecodeFailed += (_, _) => done.TrySetResult();
        return done.Task;
    }

    private static BitmapImage LoadPreview(Uri source)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = source;
        image.DecodePixelWidth = PreviewDecodeWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        return image;
    }

    // Remote bitmaps decode on the UI thread, so the result is frozen for the save dialog on the app's thread.
    private static async Task<BitmapSource?> LoadFullImageAsync(Uri source)
    {
        var image = new BitmapImage();
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        image.DownloadCompleted += (_, _) => loaded.TrySetResult(true);
        image.DownloadFailed += (_, _) => loaded.TrySetResult(false);
        image.DecodeFailed += (_, _) => loaded.TrySetResult(false);
        try
        {
            image.BeginInit();
            image.UriSource = source;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            if (image.IsDownloading && !await loaded.Task.WaitAsync(FullImageWait)) return null;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or
                                              UnauthorizedAccessException or TimeoutException)
        {
            return null;
        }
        if (!image.CanFreeze) return null;
        image.Freeze();
        return image;
    }

    private static double AspectRatio(PinterestPin pin) =>
        pin.Width > 0 && pin.Height > 0 ? (double)pin.Width / pin.Height : 1;

    private static TextBlock Text(string value, double size, Color color) => new()
    {
        Text = value, FontFamily = PluginTypography.Font, FontSize = size,
        Foreground = OverlayVisualResources.Frozen(color), TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private void CloseResult()
    {
        if (!_host.IsActive) return;
        DismissResult();
        _close();
    }

    public void DismissResult()
    {
        _menu?.Close();
        _host.Dismiss();
    }

    public void Dispose()
    {
        _host.Dispose();
        _menu?.Dispose();
        _scrollbar?.Dispose();
        _scrollMotion?.Dispose();
    }
}
