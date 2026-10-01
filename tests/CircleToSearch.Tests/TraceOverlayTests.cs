using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Effects;
using CircleToSearch.Ui;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class TraceOverlayTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Renders_card_and_loading_and_copy_does_not_open_anilist(bool light, bool hostButtonAlignment)
    {
        RunSta(time =>
        {
            _ = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
            var visual = OverlayVisualFactory.CreateRoot(null, new Size(960, 600), 32, light,
                TestUiStrings.English, [new(SearchProviderIds.TraceMoe, "trace.moe")], SearchProviderIds.TraceMoe);
            var window = new Window { Content = new AdornerDecorator { Child = visual.Root }, Width = 960, Height = 600, ShowActivated = false, ShowInTaskbar = false };
            if (hostButtonAlignment)
                window.Resources.Add(typeof(Button), new Style(typeof(Button))
                {
                    Setters = { new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left) },
                });
            window.Show();
            window.UpdateLayout();
            int opened = 0, closed = 0;
            string? copied = null;
            var notifications = new List<ToastNotification>();
            var clipboardCopy = new ClipboardCopyService(text => copied = text, notifications.Add, TestUiStrings.English);
            using var activity = new OverlayActivityPresenter(visual.ActivityHost, OverlayVisualResources.AnimationsEnabled);
            using var trace = TraceOverlayVisual.Create(new OverlayWidgetContext(visual.Root, activity, visual.Bottom, visual.Effects, TestUiStrings.English, light,
                _ => opened++, () => closed++, clipboardCopy), video => new TraceVideoPreview(video,
                    () => Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                        userDataFolder: Path.Combine(TestOutputPaths.TempDirectory, "trace-video-profile")),
                    new PluginLog(TestOutputPaths.TempDirectory)));
            try
            {
                time.Advance(240);
                Assert.Single(Descendants(visual.Root).OfType<LoadingIndicatorVisual>(), x => x.IsRequestedActive);
                var loadingText = Descendants(visual.Root).OfType<TextBlock>().Single(x => x.Text == TestUiStrings.English.TraceSearching);
                Assert.Equal("Searching...", loadingText.Text);
                Assert.Equal(PluginPalette.ListeningText, Assert.IsType<SolidColorBrush>(loadingText.Foreground).Color);
                var shadow = Assert.IsType<DropShadowEffect>(loadingText.Effect);
                Assert.Equal(10, shadow.BlurRadius);
                Assert.Equal(1, shadow.ShadowDepth);
                Capture(visual.Root, $"trace-{light}-loading.png");
                var live = Environment.GetEnvironmentVariable("CTS_TRACE_LIVE_PREVIEW") == "1";
                var match = TraceMoeProvider.Parse(File.ReadAllText(live
                    ? Path.Combine(TestOutputPaths.TempDirectory, "trace-live.json")
                    : Path.Combine(TestOutputPaths.RepoDirectory, "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")))!;
                trace.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(live ? match : match with { Image = null, Video = null })));
                if (live)
                {
                    var media = Assert.Single(Descendants(visual.Root).OfType<Microsoft.Web.WebView2.Wpf.WebView2CompositionControl>());
                    DispatcherPump.For(6000);
                    Assert.NotNull(media.CoreWebView2);
                    var state = media.CoreWebView2.ExecuteScriptAsync("JSON.stringify({ready:document.querySelector('video').readyState,muted:document.querySelector('video').muted,time:document.querySelector('video').currentTime,loop:document.querySelector('video').loop})");
                    Assert.True(DispatcherPump.Until(() => state.IsCompleted));
                    File.WriteAllText(Path.Combine(TestOutputPaths.TempDirectory, "trace-video-state.txt"), state.Result);
                    var decoded = System.Text.Json.JsonSerializer.Deserialize<string>(state.Result)!;
                    using var playback = System.Text.Json.JsonDocument.Parse(decoded);
                    Assert.True(playback.RootElement.GetProperty("ready").GetInt32() >= 2);
                    Assert.True(playback.RootElement.GetProperty("time").GetDouble() > 0);
                    Assert.True(playback.RootElement.GetProperty("muted").GetBoolean());
                    Assert.True(playback.RootElement.GetProperty("loop").GetBoolean());
                    Assert.True(media.CoreWebView2.IsMuted);
                }
                Reveal(time, trace);
                Assert.True(trace.Presentation.IsCompletedSuccessfully, trace.Presentation.Exception?.ToString());
                Assert.DoesNotContain(Descendants(visual.Root).OfType<LoadingIndicatorVisual>(), x => x.IsRequestedActive);
                Assert.Contains(Descendants(visual.Root).OfType<TextBlock>(), x => x.Text == match.Title);
                var card = Descendants(visual.Bottom.Stack).OfType<Border>().Single(x => x.Width == 640);
                var cardBottom = card.TranslatePoint(new Point(0, card.ActualHeight), visual.Root).Y;
                var chipsTop = visual.Bottom.ActionSlot.TranslatePoint(new Point(), visual.Root).Y;
                Assert.InRange(chipsTop - cardBottom, 15, 17);
                Assert.Equal(Visibility.Visible, visual.Bottom.Root.Visibility);
                Capture(visual.Root, $"trace-{light}-result.png");
                var segment = Descendants(card).OfType<Border>().Single(x => x.Height == 15 && x.Child is Border);
                var mouseOverKey = (DependencyPropertyKey)typeof(UIElement)
                    .GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (var hovered in new[] { true, false })
                {
                    card.SetValue(mouseOverKey, hovered);
                    time.Advance(20);
                    var surface = hovered ? PluginPalette.TraceCardHover(light) : PluginPalette.For(light).MusicOverlay.Surface;
                    Assert.Equal(surface, Assert.IsType<SolidColorBrush>(card.Background).Color);
                    Assert.Equal(surface, Assert.IsType<SolidColorBrush>(segment.Background).Color);
                    if (hovered) Capture(visual.Root, $"trace-{light}-hover.png");
                }
                var open = Descendants(card).OfType<Button>().Single(x => AutomationProperties.GetName(x).StartsWith(TestUiStrings.English.TraceOpen));
                Assert.Equal(card.ActualWidth - 2, open.ActualWidth, 1);
                Assert.Equal(open.ActualWidth - 20, Assert.IsType<Grid>(open.Content).ActualWidth, 1);
                open.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = open,
                });
                if (OverlayVisualResources.AnimationsEnabled())
                    Assert.Single(AdornerLayer.GetAdornerLayer(open)!.GetAdorners(open)!);
                open.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = open,
                });
                var copy = Descendants(visual.Root).OfType<Button>().Single(x => AutomationProperties.GetName(x) == TestUiStrings.English.TraceCopy);
                var initialCopyContent = copy.Content;
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var payload = $"{match.Title} - {string.Format(CultureInfo.CurrentCulture, TestUiStrings.English.TraceEpisode, match.Episode)}, {TraceMoeMatch.Timestamp(match.From)}";
                Assert.Equal(payload, copied);
                var toast = Assert.Single(notifications);
                Assert.Equal(TestUiStrings.English.CopiedText(payload), toast.Message);
                Assert.Equal(ToastTone.Success, toast.Tone);
                Assert.Equal(TestUiStrings.English.Copied, copy.ToolTip);
                Assert.Equal(TestUiStrings.English.Copied, AutomationProperties.GetName(copy));
                Assert.NotSame(initialCopyContent, copy.Content);
                Assert.Equal(0, opened);
                var close = Descendants(visual.Root).OfType<Button>().Last(x => AutomationProperties.GetName(x) == TestUiStrings.English.Close);
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                time.Advance(200);
                Assert.Equal(1, closed);
            }
            finally
            {
                visual.Effects.SceneRipples.Dispose();
                visual.Bottom.LayoutTransitions.Dispose();
                visual.Music.LoadingIndicator.Dispose();
                visual.Music.Waveform.Dispose();
                window.Close();
            }
        });
    }

    [Fact]
    public void Clipboard_failure_shows_error_without_copy_success_state()
    {
        RunSta(time =>
        {
            var visual = OverlayVisualFactory.CreateRoot(null, new Size(640, 400), 32, false,
                TestUiStrings.English, [new(SearchProviderIds.TraceMoe, "trace.moe")], SearchProviderIds.TraceMoe);
            var window = new Window { Content = visual.Root, Width = 640, Height = 400, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            window.UpdateLayout();
            var notifications = new List<ToastNotification>();
            var clipboardCopy = new ClipboardCopyService(
                _ => throw new InvalidOperationException(),
                notifications.Add,
                TestUiStrings.English);
            using var activity = new OverlayActivityPresenter(visual.ActivityHost, OverlayVisualResources.AnimationsEnabled);
            using var trace = TraceOverlayVisual.Create(new OverlayWidgetContext(
                visual.Root,
                activity,
                visual.Bottom,
                visual.Effects,
                TestUiStrings.English,
                false,
                _ => { },
                () => { },
                clipboardCopy));
            try
            {
                time.Advance(240);
                var match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory,
                    "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")))! with { Image = null, Video = null };
                trace.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(match)));
                Reveal(time, trace);
                var copy = Descendants(visual.Root).OfType<Button>()
                    .Single(x => AutomationProperties.GetName(x) == TestUiStrings.English.TraceCopy);
                var initialCopyContent = copy.Content;

                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var toast = Assert.Single(notifications);
                Assert.Equal(TestUiStrings.English.CopyFailed, toast.Message);
                Assert.Equal(ToastTone.Error, toast.Tone);
                Assert.Equal(TestUiStrings.English.CopyFailed, copy.ToolTip);
                Assert.Equal(TestUiStrings.English.CopyFailed, AutomationProperties.GetName(copy));
                Assert.Same(initialCopyContent, copy.Content);
            }
            finally
            {
                visual.Effects.SceneRipples.Dispose();
                visual.Bottom.LayoutTransitions.Dispose();
                visual.Music.LoadingIndicator.Dispose();
                visual.Music.Waveform.Dispose();
                window.Close();
            }
        });
    }

    [Fact]
    public void Copy_toast_stays_above_dynamic_trace_result_host()
    {
        RunSta(time =>
        {
            var visual = OverlayVisualFactory.CreateRoot(null, new Size(640, 400), 32, false,
                TestUiStrings.English, [new(SearchProviderIds.TraceMoe, "trace.moe")], SearchProviderIds.TraceMoe);
            var window = new Window { Content = visual.Root, Width = 640, Height = 400, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            window.UpdateLayout();
            var toast = new ToastOverlayController(visual.Bottom, false, () => false, TimeProvider.System);
            var clipboardCopy = new ClipboardCopyService(_ => { }, toast.Show, TestUiStrings.English);
            using var activity = new OverlayActivityPresenter(visual.ActivityHost, OverlayVisualResources.AnimationsEnabled);
            var trace = TraceOverlayVisual.Create(new OverlayWidgetContext(
                visual.Root,
                activity,
                visual.Bottom,
                visual.Effects,
                TestUiStrings.English,
                false,
                _ => { },
                () => { },
                clipboardCopy));
            try
            {
                time.Advance(240);
                var match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory,
                    "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")))! with { Image = null, Video = null };
                trace.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(match)));
                Reveal(time, trace);
                Assert.True(trace.Presentation.IsCompletedSuccessfully, trace.Presentation.Exception?.ToString());
                var copy = Descendants(visual.Bottom.Stack).OfType<Button>()
                    .Single(x => AutomationProperties.GetName(x) == TestUiStrings.English.TraceCopy);
                var actions = Assert.IsType<StackPanel>(VisualTreeHelper.GetParent(copy));
                var content = Assert.IsType<Grid>(VisualTreeHelper.GetParent(actions));
                var card = Assert.IsType<Border>(VisualTreeHelper.GetParent(content));
                var traceResultHost = Assert.IsType<Grid>(VisualTreeHelper.GetParent(card));

                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();

                var toastSlot = Assert.Single(toast.ActiveVisuals).Slot;
                Assert.Equal(
                    [toastSlot, traceResultHost, visual.Bottom.ResultSlot, visual.Bottom.ActionSlot],
                    visual.Bottom.Stack.Children.Cast<UIElement>());
                var toastBottom = toastSlot.TranslatePoint(new Point(0, toastSlot.ActualHeight), visual.Root).Y;
                var traceTop = traceResultHost.TranslatePoint(new Point(), visual.Root).Y;
                Assert.True(toastBottom < traceTop, $"Toast bottom {toastBottom} must be above trace top {traceTop}.");
            }
            finally
            {
                trace.Dispose();
                toast.Dispose();
                visual.Effects.SceneRipples.Dispose();
                visual.Bottom.LayoutTransitions.Dispose();
                visual.Music.LoadingIndicator.Dispose();
                visual.Music.Waveform.Dispose();
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Trace_result_keeps_overlay_open_and_supports_close_retry_and_music(bool startMusic, bool matched)
    {
        RunSta(time =>
        {
            using var frame = new System.Drawing.Bitmap(640, 400);
            var monitor = new System.Drawing.Rectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(frame, monitor, monitor, 1,
                new OverlayLaunchOptions(new OverlayOptions(8, 12), TestUiStrings.English,
                    [new(SearchProviderIds.TraceMoe, "trace.moe"), new(SearchProviderIds.GoogleLens, "Google Lens")],
                    SearchProviderIds.TraceMoe),
                commands.Add, TestOverlayControllers.CreateFactory(), overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            var lasso = (SelectionOverlayController)typeof(OverlayWindow)
                .GetField("_selection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
            Assert.True(lasso.Begin(new Point(10, 10)));
            lasso.Update(new Point(110, 10));
            lasso.Update(new Point(110, 110));
            lasso.Complete(new Point(10, 110));
            Assert.NotEmpty(overlay.VisualState.Selection.Accent.Points);
            Assert.Equal(2, commands.Count);
            Assert.IsType<VisualSelectionStarted>(commands[0]);
            var selection = Assert.IsType<VisualSelection>(commands[1]);
            Assert.NotSame(frame, selection.Selection.FrozenFrame);
            selection.Selection.Dispose();
            typeof(OverlayWindow).GetMethod("OnSelectionHoldCompleted", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(overlay, null);
            time.Advance(520);
            Assert.False(overlay.Dispatcher.HasShutdownStarted);
            Assert.Equal(OverlayInteractionMode.WidgetLoading, overlay.Mode);
            Assert.Equal(Visibility.Visible, overlay.VisualState.Bottom.Root.Visibility);
            Assert.True(overlay.VisualState.Actions.Tray.Opacity > 0.99);
            Assert.False(overlay.FrameTransferred);
            Assert.Equal(640, frame.Width);
            var match = matched ? TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory,
                "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")))! with { Image = null, Video = null } : null;
            var resultText = match?.Title ?? TestUiStrings.English.TraceNoMatch;
            overlay.ShowWidgetResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(match)));
            Assert.Equal(OverlayInteractionMode.WidgetResult, overlay.Mode);
            Assert.Contains(Descendants(overlay.VisualState.Root).OfType<TextBlock>(), x => x.Text == resultText);
            if (!matched)
            {
                var stateCard = Descendants(overlay.VisualState.Bottom.Stack).OfType<Border>()
                    .Single(x => AutomationProperties.GetName(x) == TestUiStrings.English.TraceMoeProviderName);
                Assert.Equal(340, stateCard.Width);
                Assert.Equal(new CornerRadius(18), stateCard.CornerRadius);
                Assert.Contains(Descendants(stateCard).OfType<System.Windows.Shapes.Path>(),
                    icon => ReferenceEquals(icon.Data, PluginIcons.TraceMoe));
            }
            time.Advance(450);
            var provider = overlay.VisualState.Provider!;
            Assert.True(provider.Button.IsEnabled);
            provider.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, provider.Menu.Visibility);
            provider.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(overlay.VisualState.Music.Button.IsEnabled);
            if (startMusic)
            {
                overlay.VisualState.Music.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
                Assert.Single(commands.OfType<StartMusicRecognition>());
            }
            else
            {
                var close = Descendants(overlay.VisualState.Bottom.Stack).OfType<Button>()
                    .Single(x => AutomationProperties.GetName(x) == TestUiStrings.English.Close);
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
                Assert.True(overlay.VisualState.TranslationAction.Button.IsEnabled);
                typeof(OverlayWindow).GetMethod("OnSelectionHoldCompleted", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(overlay, null);
            }
            time.Advance(300);
            Assert.False(overlay.Dispatcher.HasShutdownStarted);
            Assert.DoesNotContain(commands, x => x is CancelSession);
            Assert.DoesNotContain(Descendants(overlay.VisualState.Root).OfType<TextBlock>(), x => x.Text == resultText);
            Assert.Empty(overlay.VisualState.Selection.Accent.Points);
            Assert.Empty(overlay.VisualState.Selection.Halo.Points);
            Assert.True(overlay.VisualState.Selection.Sheen.Data.IsEmpty());
            Assert.True(overlay.VisualState.Selection.DimRect.Data.IsEmpty());
            Assert.True(overlay.VisualState.Selection.SelectionFrame.Data.IsEmpty());
            Assert.True(overlay.VisualState.Selection.Dim.Data.FillContains(new Point(50, 50)));
            Assert.False(lasso.HasPendingHold);
            Assert.False(lasso.HasPendingRevealUpdate);
            if (!startMusic)
            {
                Assert.True(lasso.Begin(new Point(150, 10)));
                Assert.Single(overlay.VisualState.Selection.Accent.Points);
                lasso.Update(new Point(250, 10));
                lasso.Complete(new Point(250, 100));
                Assert.Equal(OverlayInteractionMode.WidgetLoading, overlay.Mode);
                Assert.Equal(2, commands.OfType<VisualSelection>().Count());
                commands.OfType<VisualSelection>().Last().Selection.Dispose();
            }
            overlay.CloseFromSession();
            Assert.True(time.AdvanceUntil(() => overlay.Dispatcher.HasShutdownStarted), "The overlay did not finish closing.");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Waits_for_preview_then_fades_loading_before_revealing_card(bool videoReady)
    {
        RunSta(time =>
        {
            var visual = OverlayVisualFactory.CreateRoot(null, new Size(960, 600), 32, false,
                TestUiStrings.English, [new(SearchProviderIds.TraceMoe, "trace.moe")], SearchProviderIds.TraceMoe);
            var window = new Window { Content = visual.Root, Width = 960, Height = 600, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            window.UpdateLayout();
            var preview = new DeferredPreview();
            var clipboardCopy = new ClipboardCopyService(_ => { }, _ => { }, TestUiStrings.English);
            using var activity = new OverlayActivityPresenter(visual.ActivityHost, OverlayVisualResources.AnimationsEnabled);
            using var trace = TraceOverlayVisual.Create(new OverlayWidgetContext(visual.Root, activity, visual.Bottom, visual.Effects, TestUiStrings.English,
                false, _ => { }, () => { }, clipboardCopy), _ => preview);
            try
            {
                time.Advance(240);
                var loading = Descendants(visual.Root).OfType<TextBlock>().Single(x => x.Text == TestUiStrings.English.TraceSearching);
                var loadingPanel = Assert.IsType<Grid>(loading.Parent);
                var loadingIndicator = Assert.Single(Descendants(visual.Root).OfType<LoadingIndicatorVisual>(), x => x.IsRequestedActive);
                var match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory,
                    "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")))! with { Image = null };
                trace.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(match)));
                time.Advance(250);
                var card = Descendants(visual.Bottom.Stack).OfType<Border>().Single(x => x.Width == 640);
                var slot = (Grid)card.Parent;
                Assert.Equal(0, slot.Opacity);
                Assert.False(slot.IsHitTestVisible);
                Assert.True(loadingPanel.IsVisible);
                Assert.False(trace.Presentation.IsCompleted);
                preview.Complete(videoReady);
                // Readiness reaches the dispatcher through a thread-pool continuation; the loading fade starts after it.
                Assert.True(DispatcherPump.Until(() => !loadingIndicator.IsRequestedActive));
                time.Advance(50);
                if (OverlayVisualResources.AnimationsEnabled())
                {
                    Assert.True(loadingPanel.HasAnimatedProperties);
                    Assert.InRange(loadingPanel.Opacity, 0.01, 0.99);
                    Assert.Equal(0, slot.Opacity);
                }
                Assert.True(time.AdvanceUntil(() => trace.Presentation.IsCompleted));
                Assert.True(trace.Presentation.IsCompletedSuccessfully, trace.Presentation.Exception?.ToString());
                Assert.Equal(1, slot.Opacity);
                Assert.True(slot.IsHitTestVisible);
                Assert.False(loadingPanel.IsVisible);
                Assert.Equal(!videoReady, preview.Disposed);
            }
            finally
            {
                trace.Dispose();
                visual.Effects.SceneRipples.Dispose();
                visual.Bottom.LayoutTransitions.Dispose();
                visual.Music.LoadingIndicator.Dispose();
                visual.Music.Waveform.Dispose();
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closing_while_video_is_pending_cancels_reveal_and_releases_preview(bool beforeQueuedReveal)
    {
        RunSta(time =>
        {
            var visual = OverlayVisualFactory.CreateRoot(null, new Size(640, 400), 32, false,
                TestUiStrings.English, [new(SearchProviderIds.TraceMoe, "trace.moe")], SearchProviderIds.TraceMoe);
            var window = new Window { Content = visual.Root, Width = 640, Height = 400, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            window.UpdateLayout();
            var preview = new DeferredPreview();
            var clipboardCopy = new ClipboardCopyService(_ => { }, _ => { }, TestUiStrings.English);
            using var activity = new OverlayActivityPresenter(visual.ActivityHost, OverlayVisualResources.AnimationsEnabled);
            var trace = TraceOverlayVisual.Create(new OverlayWidgetContext(visual.Root, activity, visual.Bottom, visual.Effects, TestUiStrings.English,
                false, _ => { }, () => { }, clipboardCopy), _ => preview);
            var match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory,
                "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")))! with { Image = null };
            trace.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(match)));
            if (!beforeQueuedReveal) time.Advance(50);
            trace.Dispose();
            preview.Complete(true);
            Assert.True(time.AdvanceUntil(() => trace.Presentation.IsCompleted));
            time.Advance(250);
            Assert.True(trace.Presentation.IsCompletedSuccessfully, trace.Presentation.Exception?.ToString());
            Assert.True(preview.Disposed);
            Assert.Equal(2, visual.Bottom.Stack.Children.Count);
            visual.Effects.SceneRipples.Dispose();
            visual.Bottom.LayoutTransitions.Dispose();
            visual.Music.LoadingIndicator.Dispose();
            visual.Music.Waveform.Dispose();
            window.Close();
        });
    }

    private sealed class DeferredPreview : ITraceVideoPreview
    {
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FrameworkElement Root { get; } = new Grid();
        public Task<bool> Ready => _ready.Task;
        public bool Disposed { get; private set; }
        public void Complete(bool success) => _ready.TrySetResult(success);
        public void Dispose() => Disposed = true;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void Capture(FrameworkElement root, string name)
    {
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(TestOutputPaths.TempDirectory);
        using var stream = File.Create(Path.Combine(TestOutputPaths.TempDirectory, name));
        encoder.Save(stream);
    }

    // The reveal waits on thread-pool continuations, so the clock keeps running until it lands;
    // then the card entrance gets its full duration.
    private static void Reveal(ManualAnimationClock time, TraceOverlayVisual trace)
    {
        Assert.True(time.AdvanceUntil(() => trace.Presentation.IsCompleted), "The trace result was not revealed.");
        time.Advance((int)StateCardTransitions.EntranceDuration.TotalMilliseconds);
    }

    private static void RunSta(Action<ManualAnimationClock> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var time = ManualAnimationClock.Install();
                action(time);
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
