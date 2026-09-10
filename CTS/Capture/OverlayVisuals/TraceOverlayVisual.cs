using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;

namespace CircleToSearch.Capture;

internal sealed class TraceOverlayVisual : IDisposable
{
    private readonly Grid _root;
    private readonly OverlayEffectsVisual _effects;
    private readonly BottomOverlayVisual _bottom;
    private readonly UiStrings _strings;
    private readonly bool _light;
    private readonly Action _open;
    private readonly Action _close;
    private readonly ClipboardCopyService _clipboardCopy;
    private readonly Grid _host = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly LoadingIndicatorVisual _loading = new() { Width = 80, Height = 80 };
    private ITraceVideoPreview? _media;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Grid _resultHost = new()
    {
        Visibility = Visibility.Collapsed, Opacity = 0, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private StackPanel? _loadingPanel;
    private MusicResultTransitions.ExitHandle? _loadingExit;
    internal Task Presentation { get; private set; } = Task.CompletedTask;
    private Func<Uri, ITraceVideoPreview>? _createVideo;
    private Border? _card;
    private MusicResultTransitions.ExitHandle? _exit;
    private DispatcherOperation? _ripple;
    private readonly List<IDisposable> _controlRipples = [];
    private bool _disposed;
    private bool _closing;

    private TraceOverlayVisual(Grid root, BottomOverlayVisual bottom, OverlayEffectsVisual effects, UiStrings strings,
        bool light, Action open, Action close, ClipboardCopyService clipboardCopy)
    {
        _root = root;
        _bottom = bottom;
        _resultHost.Margin = bottom.ResultSlot.Margin;
        _effects = effects;
        _strings = strings;
        _light = light;
        _open = open;
        _close = close;
        _clipboardCopy = clipboardCopy ?? throw new ArgumentNullException(nameof(clipboardCopy));
    }

    internal static TraceOverlayVisual Create(Grid root, BottomOverlayVisual bottom, OverlayEffectsVisual effects, UiStrings strings,
        bool light, Action open, Action close, ClipboardCopyService clipboardCopy, Func<Uri, ITraceVideoPreview>? createVideo = null)
    {
        var visual = new TraceOverlayVisual(root, bottom, effects, strings, light, open, close, clipboardCopy);
        visual._createVideo = createVideo;
        visual.ShowLoading();
        return visual;
    }

    private void ShowLoading()
    {
        Panel.SetZIndex(_host, 30);
        _root.Children.Add(_host);
        _loading.Fill = OverlayVisualResources.Frozen(PluginPalette.For(_light).MusicOverlay.Primary);
        var panel = _loadingPanel = new StackPanel();
        panel.Children.Add(_loading);
        var label = Text(_strings.TraceSearching, 14);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, 10, 0, 0);
        label.Foreground = OverlayVisualResources.Frozen(PluginPalette.ListeningText);
        label.FontWeight = FontWeights.Medium;
        label.Effect = new DropShadowEffect
        {
            Color = PluginPalette.OpaqueBlack, BlurRadius = 10, ShadowDepth = 1, Opacity = 0.4,
        };
        panel.Children.Add(label);
        _host.Children.Add(panel);
        _loading.Start(OverlayVisualResources.AnimationsEnabled());
        MusicResultTransitions.BeginEntrance(panel, OverlayVisualResources.AnimationsEnabled());
    }

    internal void ShowResult(VisualSearchPreparationOutcome outcome)
    {
        if (_disposed || _closing || _card is not null) return;
        var palette = PluginPalette.For(_light).MusicOverlay;
        var match = outcome.PreparedSearch?.TraceMatch;
        var content = new Grid();
        _card = new Border
        {
            Child = content, Width = Math.Min(640, Math.Max(240, _root.ActualWidth - 32)),
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(PluginPalette.For(_light).SelectionChip.Divider),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24),
            Padding = new Thickness(0), Effect = OverlayVisualResources.DockShadow(10, palette.ShadowOpacity),
        };

        if (match is not null)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_card.Width < 480 ? 120 : 172) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(CreateMedia(match, row.ColumnDefinitions[0].Width.Value));
            var info = new StackPanel { Margin = new Thickness(14, 12, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(info, 1);
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 50, 0) };
            var percent = new Border
            {
                Child = Text(match.Similarity.ToString("P1", CultureInfo.CurrentCulture), 12.5, primary: true),
                BorderBrush = OverlayVisualResources.Frozen(palette.Primary), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(9, 2, 9, 2), Margin = new Thickness(0, 0, 8, 0),
            };
            DockPanel.SetDock(percent, Dock.Left);
            titleRow.Children.Add(percent);
            var title = Text(match.Title, 15.5);
            title.FontWeight = FontWeights.Bold;
            title.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(title);
            info.Children.Add(titleRow);
            info.Children.Add(Text(match.NativeTitle, 12, muted: true));
            var episode = string.IsNullOrEmpty(match.Episode) ? "" : string.Format(CultureInfo.CurrentCulture, _strings.TraceEpisode, match.Episode);
            info.Children.Add(Text(string.Join(" · ", new[] { episode, match.Format, match.Year, match.Studio }.Where(s => !string.IsNullOrEmpty(s))), 12, muted: true));
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
            OverlayVisualResources.ApplyButtonTemplate(openButton, 24,
                PluginPalette.Composite(palette.Surface, PluginPalette.For(_light).SelectionChip.KeycapBackground), palette.Primary);
            AutomationProperties.SetName(openButton, $"{_strings.TraceOpen}: {match.Title}");
            openButton.Click += (_, e) => { if (!_closing) _open(); e.Handled = true; };
            content.Children.Add(openButton);
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
            var label = Text(message, 14);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(20, 26, 50, 26);
            content.Children.Add(label);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) };
        if (match is not null)
        {
            var copy = MusicOverlayVisualPresenter.IconButton(MusicOverlayVisualPresenter.CopyIconGeometry, _strings.TraceCopy, palette, 12);
            var copyIcon = copy.Content;
            copy.Click += (_, e) =>
            {
                e.Handled = true;
                var payload = $"{match.Title} — {string.Format(CultureInfo.CurrentCulture, _strings.TraceEpisode, match.Episode)}, {TraceMoeMatch.Timestamp(match.From)}";
                if (_clipboardCopy.TryCopy(payload))
                {
                    copy.ToolTip = _strings.Copied;
                    AutomationProperties.SetName(copy, _strings.Copied);
                    copy.Content = OverlayVisualResources.Icon(MusicOverlayVisualPresenter.CheckIconGeometry, 12, palette.Primary);
                }
                else
                {
                    copy.ToolTip = _strings.CopyFailed;
                    AutomationProperties.SetName(copy, _strings.CopyFailed);
                    copy.Content = copyIcon;
                }
            };
            actions.Children.Add(copy);
        }
        var close = MusicOverlayVisualPresenter.IconButton(MusicOverlayVisualPresenter.CloseIconGeometry, _strings.Close, palette, 12);
        close.Click += (_, e) =>
        {
            e.Handled = true;
            if (_closing) return;
            DismissResult();
            _close();
        };
        actions.Children.Add(close);
        content.Children.Add(actions);
        _resultHost.Children.Add(_card);
        _bottom.LayoutTransitions.Apply(() =>
        {
            _bottom.Stack.Children.Insert(_bottom.Stack.Children.IndexOf(_bottom.ResultSlot), _resultHost);
            _resultHost.Visibility = Visibility.Visible;
        }, OverlayVisualResources.AnimationsEnabled());
        Presentation = _root.Dispatcher.InvokeAsync(() => RevealWhenReadyAsync(match is not null)).Task.Unwrap();
    }

    private async Task RevealWhenReadyAsync(bool matched)
    {
        if (_disposed || _closing) return;
        try
        {
            if (_media is not null)
            {
                bool ready;
                try { ready = await _media.Ready.WaitAsync(TimeSpan.FromSeconds(8), _lifetime.Token); }
                catch (TimeoutException) { ready = false; }
                if (!ready) _media.Dispose();
            }
            if (_disposed || _closing) return;
            if (_loadingPanel is not null)
            {
                _loading.BeginStop();
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _loadingExit = MusicResultTransitions.BeginExit(_loadingPanel,
                    OverlayVisualResources.AnimationsEnabled(), () => finished.TrySetResult());
                await finished.Task.WaitAsync(_lifetime.Token);
                if (_disposed || _closing) return;
                _loadingExit.Dispose();
                _loadingExit = null;
            }
            _loading.Stop();
            _root.Children.Remove(_host);
            _resultHost.Opacity = 1;
            _resultHost.IsHitTestVisible = true;
            _controlRipples.AddRange(OverlayVisualResources.AttachControlRipples(_resultHost));
            MusicResultTransitions.BeginEntrance(_card!, OverlayVisualResources.AnimationsEnabled());
            if (!matched || !OverlayVisualResources.AnimationsEnabled()) return;
            _ripple = _root.Dispatcher.BeginInvoke(() =>
            {
                _ripple = null;
                if (_disposed || _closing) return;
                _resultHost.UpdateLayout();
                var origin = _card!.TranslatePoint(new Point(_card.ActualWidth / 2, _card.ActualHeight / 2), _root);
                _effects.SceneRipples.Emit(new SceneRippleRequest(origin, SceneRipplePreset.MusicMatch, 1));
            }, DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private FrameworkElement CreateMedia(TraceMoeMatch match, double width)
    {
        var grid = new Grid { Width = width, Height = 97, Clip = new RectangleGeometry(new Rect(0, 0, width, 97), 14, 14) };
        if (match.Image is not null)
            grid.Children.Add(new Image { Source = new BitmapImage(match.Image), Stretch = Stretch.UniformToFill });
        if (match.Video is not null && _createVideo is not null)
        {
            _media = _createVideo(match.Video);
            grid.Children.Add(_media.Root);
        }
        grid.Children.Add(new Border
        {
            Child = Text(TraceMoeMatch.Timestamp(match.From), 11), CornerRadius = new CornerRadius(7),
            Background = OverlayVisualResources.Frozen(PluginPalette.For(_light).MusicOverlay.Surface),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8),
        });
        return grid;
    }

    private FrameworkElement CreateTimeline(TraceMoeMatch match)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
        var track = new Grid { Height = 8, Background = OverlayVisualResources.Frozen(PluginPalette.For(_light).SelectionChip.KeycapBackground) };
        var segment = new Border { Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left,
            Background = OverlayVisualResources.Frozen(PluginPalette.For(_light).MusicOverlay.Primary) };
        track.Children.Add(segment);
        track.SizeChanged += (_, _) =>
        {
            var duration = Math.Max(1, match.Duration);
            segment.Width = Math.Min(track.ActualWidth, Math.Max(26, track.ActualWidth * (match.To - match.From) / duration));
            segment.Margin = new Thickness(Math.Clamp(track.ActualWidth * match.From / duration, 0, Math.Max(0, track.ActualWidth - segment.Width)), 0, 0, 0);
        };
        panel.Children.Add(track);
        var scale = new Grid();
        scale.Children.Add(Text(TraceMoeMatch.Timestamp(0), 10.5, muted: true));
        var end = Text(TraceMoeMatch.Timestamp(match.Duration), 10.5, muted: true);
        end.HorizontalAlignment = HorizontalAlignment.Right;
        scale.Children.Add(end);
        panel.Children.Add(scale);
        return panel;
    }

    private TextBlock Text(string value, double size, bool muted = false, bool primary = false)
    {
        var palette = PluginPalette.For(_light).MusicOverlay;
        return new TextBlock { Text = value, FontFamily = OverlayVisualResources.Font, FontSize = size,
            Foreground = OverlayVisualResources.Frozen(primary ? palette.Primary : muted ? palette.MutedText : palette.Text),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
    }

    internal void DismissResult()
    {
        if (_disposed || _closing || _card is null) return;
        _closing = true;
        _lifetime.Cancel();
        _ripple?.Abort();
        _resultHost.IsHitTestVisible = false;
        _media?.Dispose();
        _exit = MusicResultTransitions.BeginExit(_card, OverlayVisualResources.AnimationsEnabled(),
            () => _bottom.LayoutTransitions.Apply(Dispose, OverlayVisualResources.AnimationsEnabled()));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _loadingExit?.Dispose();
        _loading.Dispose();
        _media?.Dispose();
        _exit?.Dispose();
        _ripple?.Abort();
        foreach (var ripple in _controlRipples) ripple.Dispose();
        _root.Children.Remove(_host);
        _bottom.Stack.Children.Remove(_resultHost);
        _lifetime.Dispose();
    }
}
