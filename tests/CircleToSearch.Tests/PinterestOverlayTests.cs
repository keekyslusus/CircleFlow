using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class PinterestOverlayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Strip_opens_pins_and_expands_into_masonry(bool light)
    {
        RunSta(time =>
        {
            var pins = Pins();
            var harness = Harness.Create(960, 600, light);
            var opened = new List<Uri>();
            var closed = 0;
            using var pinterest = PinterestOverlayVisual.Create(harness.Context(opened.Add, () => closed++));
            try
            {
                time.Advance(240);
                Assert.Contains(Descendants(harness.Visual.Root).OfType<TextBlock>(),
                    text => text.Text == TestUiStrings.English.PinterestSearching);
                pinterest.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForPinterest(pins)));
                Reveal(time, pinterest);

                var card = Descendants(harness.Visual.Bottom.Stack).OfType<Border>()
                    .Single(border => AutomationProperties.GetName(border) == TestUiStrings.English.PinterestProviderName);
                Assert.Equal(640, card.Width);
                Assert.Contains(Descendants(card).OfType<TextBlock>(),
                    text => text.Text == TestUiStrings.English.PinterestSummary(pins.Length));
                var strip = PinButtons(card);
                var more = Descendants(card).OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.PinterestShowAll);
                Assert.InRange(strip.Count, 2, pins.Length - 1);
                Assert.Contains(Descendants(more).OfType<TextBlock>(),
                    text => text.Text == TestUiStrings.English.PinterestMore(pins.Length - strip.Count));
                var stripRight = more.TranslatePoint(new Point(more.ActualWidth, 0), card).X;
                Assert.InRange(stripRight, 600, card.ActualWidth - 16);
                Assert.All(strip, button => Assert.Equal(PinterestOverlayVisual.StripHeight, button.ActualHeight, 1));
                Capture(harness.Visual.Root, $"pinterest-{light}-strip.png");

                var hovered = strip[1];
                var outline = Descendants(hovered).OfType<Border>().Single(border => border.BorderThickness.Left == 2);
                var details = Descendants(hovered).OfType<Grid>().Single(grid => grid.Background is LinearGradientBrush);
                Assert.Equal(0, outline.Opacity);
                Assert.Equal(0, details.Opacity);
                hovered.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
                if (OverlayVisualResources.AnimationsEnabled())
                {
                    time.Advance(60);
                    Assert.InRange(outline.Opacity, 0.01, 0.99);
                    Assert.InRange(details.Opacity, 0.01, 0.99);
                }
                time.Advance(200);
                Assert.Equal(1, outline.Opacity, 3);
                Assert.Equal(1, details.Opacity, 3);
                Capture(harness.Visual.Root, $"pinterest-{light}-hover.png");
                hovered.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
                time.Advance(200);
                Assert.Equal(0, outline.Opacity, 3);
                Assert.Equal(0, details.Opacity, 3);

                strip[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(new Uri(pins[1].PinUrl), Assert.Single(opened));
                var back = Descendants(card).OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.PinterestBack);
                Assert.Equal(Visibility.Collapsed, back.Visibility);

                var stripPreview = Descendants(strip[0]).OfType<Image>().Single().Source;
                Assert.Equal(1, Descendants(strip[0]).OfType<Image>().Single().Opacity);
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                time.Advance(400);
                harness.Window.UpdateLayout();
                Assert.True(pinterest.IsExpanded);
                Assert.Contains(PinButtons(card), button => ReferenceEquals(Descendants(button).OfType<Image>().Single().Source, stripPreview));
                var scroll = Assert.Single(Descendants(card).OfType<ScrollViewer>());
                Assert.Equal(PinterestOverlayVisual.MasonryMaxHeight, scroll.ActualHeight, 1);
                var fadeEdge = Assert.IsType<LinearGradientBrush>(scroll.OpacityMask).GradientStops[0];
                Assert.Equal(1 - 48 / PinterestOverlayVisual.MasonryMaxHeight, fadeEdge.Offset, 3);
                Assert.All(PinButtons(card), button => Assert.Null(button.ToolTip));
                Assert.Equal(pins.Length, PinButtons(card).Count);
                Assert.DoesNotContain(Descendants(card).OfType<Button>(),
                    button => AutomationProperties.GetName(button) == TestUiStrings.English.PinterestShowAll);
                Capture(harness.Visual.Root, $"pinterest-{light}-expanded.png");
                Assert.Equal(Visibility.Visible, back.Visibility);
                var bar = (System.Windows.Controls.Primitives.ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
                Assert.Equal(0, bar.Opacity);
                scroll.ScrollToEnd();
                harness.Window.UpdateLayout();
                if (OverlayVisualResources.AnimationsEnabled())
                {
                    time.Advance(80);
                    Assert.InRange(fadeEdge.Offset, 1 - 48 / PinterestOverlayVisual.MasonryMaxHeight + 0.001, 0.999);
                }
                time.Advance(240);
                Assert.Equal(1, fadeEdge.Offset, 3);
                Assert.Equal(1, bar.Opacity, 3);
                Assert.True(bar.IsHitTestVisible);
                Capture(harness.Visual.Root, $"pinterest-{light}-scrolled.png");

                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                time.Advance(400);
                harness.Window.UpdateLayout();
                Assert.False(pinterest.IsExpanded);
                Assert.Equal(Visibility.Collapsed, back.Visibility);
                Assert.Empty(Descendants(card).OfType<ScrollViewer>());
                Assert.Same(more, Descendants(card).OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.PinterestShowAll));
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                time.Advance(400);
                harness.Window.UpdateLayout();
                Assert.Same(scroll, Assert.Single(Descendants(card).OfType<ScrollViewer>()));
                var pressed = PinButtons(card)[0];
                pressed.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = pressed });
                var adorners = System.Windows.Documents.AdornerLayer.GetAdornerLayer(scroll)!;
                Assert.Empty(adorners.GetAdorners(scroll) ?? []);
                if (OverlayVisualResources.AnimationsEnabled())
                    Assert.Single(System.Windows.Documents.AdornerLayer.GetAdornerLayer(pressed)!.GetAdorners(pressed)!);
                pressed.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = pressed });

                var close = Descendants(card).OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close);
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                time.Advance(300);
                Assert.Equal(1, closed);
                Assert.Single(opened);
            }
            finally
            {
                harness.Dispose();
            }
        });
    }

    [Fact]
    public void All_pins_fit_without_a_more_tile()
    {
        RunSta(time =>
        {
            var pins = Pins().Take(2).ToArray();
            var harness = Harness.Create(960, 600, light: false);
            using var pinterest = PinterestOverlayVisual.Create(harness.Context(_ => { }, () => { }));
            try
            {
                pinterest.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForPinterest(pins)));
                Reveal(time, pinterest);
                Assert.Equal(2, PinButtons(harness.Visual.Bottom.Stack).Count);
                Assert.DoesNotContain(Descendants(harness.Visual.Bottom.Stack).OfType<Button>(),
                    button => AutomationProperties.GetName(button) == TestUiStrings.English.PinterestShowAll);
            }
            finally
            {
                harness.Dispose();
            }
        });
    }

    [Theory]
    [InlineData(UploadFailure.None)]
    [InlineData(UploadFailure.NetworkError)]
    public void Empty_or_failed_search_shows_state_card(UploadFailure failure)
    {
        RunSta(time =>
        {
            var harness = Harness.Create(640, 400, light: false);
            var closed = 0;
            using var pinterest = PinterestOverlayVisual.Create(harness.Context(_ => { }, () => closed++));
            try
            {
                pinterest.ShowResult(failure == UploadFailure.None
                    ? VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForPinterest([]))
                    : VisualSearchPreparationOutcome.Fail(failure));
                Reveal(time, pinterest);
                var message = failure == UploadFailure.None
                    ? TestUiStrings.English.PinterestNoMatch
                    : TestUiStrings.English.SearchNetworkError;
                Assert.Contains(Descendants(harness.Visual.Bottom.Stack).OfType<TextBlock>(), text => text.Text == message);
                Assert.Contains(Descendants(harness.Visual.Bottom.Stack).OfType<System.Windows.Shapes.Path>(),
                    icon => ReferenceEquals(icon.Data, PluginIcons.PinterestMark));
                Descendants(harness.Visual.Bottom.Stack).OfType<Button>()
                    .Last(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                time.Advance(300);
                Assert.Equal(1, closed);
            }
            finally
            {
                harness.Dispose();
            }
        });
    }

    private static List<Button> PinButtons(DependencyObject root) =>
        Descendants(root).OfType<Button>()
            .Where(button => AutomationProperties.GetName(button).StartsWith(TestUiStrings.English.PinterestOpen))
            .ToList();

    // Previews come from local files so the tests stay offline; an optional folder of real pins shows true content.
    private static PinterestPin[] Pins()
    {
        var preview = Environment.GetEnvironmentVariable("CTS_PINTEREST_PREVIEW_DIRECTORY");
        var sizes = new (int Width, int Height)[]
        {
            (474, 266), (474, 474), (474, 474), (474, 355), (474, 434), (474, 474),
            (474, 520), (474, 474), (304, 304), (474, 474), (474, 700), (474, 474),
        };
        var files = preview is not null && Directory.Exists(preview)
            ? Directory.GetFiles(preview, "*.jpg").Order().ToArray()
            : [];
        return sizes.Select((size, index) =>
        {
            var source = index < files.Length ? files[index] : PreviewFile(index, size.Width, size.Height);
            return new PinterestPin((index + 1).ToString(), index == 1 ? "2B" : "", index == 0 ? "example.com" : "",
                null, new Uri(source), size.Width, size.Height);
        }).ToArray();
    }

    private static string PreviewFile(int index, int width, int height)
    {
        var path = Path.Combine(TestOutputPaths.TempDirectory, "pinterest-previews", $"pin{index}-{width}x{height}.png");
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shade = (byte)(60 + index * 15);
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = shade;
            pixels[offset + 1] = (byte)(255 - shade);
            pixels[offset + 2] = (byte)(offset / 4 % width * 255 / width);
            pixels[offset + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private sealed class Harness : IDisposable
    {
        private readonly OverlayActivityPresenter _activity;
        private readonly bool _light;

        private Harness(OverlayVisual visual, Window window, bool light)
        {
            Visual = visual;
            Window = window;
            _light = light;
            _activity = new OverlayActivityPresenter(visual.ActivityHost, OverlayVisualResources.AnimationsEnabled);
        }

        public OverlayVisual Visual { get; }
        public Window Window { get; }

        public static Harness Create(double width, double height, bool light)
        {
            var visual = OverlayVisualFactory.CreateRoot(null, new Size(width, height), 32, light,
                TestUiStrings.English, [new(SearchProviderIds.Pinterest, "Pinterest")], SearchProviderIds.Pinterest);
            var window = new Window
            {
                Content = visual.Root, Width = width, Height = height, ShowActivated = false, ShowInTaskbar = false,
            };
            window.Show();
            window.UpdateLayout();
            return new Harness(visual, window, light);
        }

        public OverlayWidgetContext Context(Action<Uri> open, Action close) => new(
            Visual.Root, _activity, Visual.Bottom, Visual.Effects, TestUiStrings.English, _light, open, close,
            new ClipboardCopyService(_ => { }, _ => { }, TestUiStrings.English));

        public void Dispose()
        {
            _activity.Dispose();
            Visual.Effects.SceneRipples.Dispose();
            Visual.Bottom.LayoutTransitions.Dispose();
            Visual.Music.LoadingIndicator.Dispose();
            Visual.Music.Waveform.Dispose();
            Window.Close();
        }
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

    private static void Reveal(ManualAnimationClock time, PinterestOverlayVisual pinterest)
    {
        Assert.True(time.AdvanceUntil(() => pinterest.Presentation.IsCompleted), "The Pinterest result was not revealed.");
        Assert.True(pinterest.Presentation.IsCompletedSuccessfully, pinterest.Presentation.Exception?.ToString());
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
