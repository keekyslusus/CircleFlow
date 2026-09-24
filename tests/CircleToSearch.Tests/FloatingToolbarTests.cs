using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class FloatingToolbarTests
{
    [Theory]
    [InlineData(320)]
    [InlineData(240)]
    [InlineData(200)]
    public void Image_actions_fit_narrow_viewports_and_restore_spacing_when_width_grows(double width)
    {
        RunOnSta(time =>
        {
            var visual = ImageSelectionVisualFactory.Create(false, TestUiStrings.English);
            visual.Toolbar.SetActionContent(visual.SearchButton, TestUiStrings.English.TextSearch,
                ProviderVisualCatalog.CreateSearchMark(SearchProviderIds.GoogleLens, false));
            var window = new Window
            {
                Width = width, Height = 240, WindowStyle = WindowStyle.None, Content = visual.Toolbar.Layer,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                Button[] buttons = [visual.SearchButton, visual.CopyButton, visual.SaveButton, visual.TranslateButton];
                foreach (var label in new[] { TestUiStrings.English.Translate, TestUiStrings.English.ShowOriginal })
                {
                    visual.TranslateButton.Content = label;
                    visual.Toolbar.Show(new Rect(width - 100, 180, 100, 40), new Size(width, 240));
                    time.Advance(260);
                    window.UpdateLayout();
                    Assert.InRange(visual.Toolbar.Surface.ActualWidth, 1, width);
                    foreach (var button in buttons)
                    {
                        var rect = button.TransformToAncestor(visual.Toolbar.Layer).TransformBounds(new Rect(button.RenderSize));
                        Assert.InRange(rect.Left, 0, width);
                        Assert.InRange(rect.Right, 0, width + 0.01);
                        Assert.InRange(rect.Top, 0, 240);
                        Assert.InRange(rect.Bottom, 0, 240);
                        Assert.True(button.IsEnabled);
                    }
                }
                if (Environment.GetEnvironmentVariable("CTS_IMAGE_SELECTION_PREVIEW") == "1")
                {
                    var bitmap = new RenderTargetBitmap((int)width, 240, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual.Toolbar.Layer);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(TestOutputPaths.TempDirectory);
                    using var output = File.Create(Path.Combine(TestOutputPaths.TempDirectory, $"image-toolbar-{width}.png"));
                    encoder.Save(output);
                }
                window.Width = 640;
                window.UpdateLayout();
                visual.Toolbar.Show(new Rect(100, 100, 100, 40), new Size(640, 240));
                time.Advance(260);
                window.UpdateLayout();
                Assert.Equal(new Thickness(12, 0, 16, 0), visual.SearchButton.Padding);
                Assert.Equal(new Thickness(12, 0, 16, 0), visual.AskButton.Padding);
                Assert.All(buttons[1..], button => Assert.Equal(new Thickness(16, 0, 16, 0), button.Padding));
                Assert.Equal(44, visual.Toolbar.Surface.ActualHeight);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(640)]
    [InlineData(240)]
    public void Ask_prompt_replaces_actions_within_viewport_and_restores_them(double width)
    {
        RunOnSta(time =>
        {
            var visual = ImageSelectionVisualFactory.Create(false, TestUiStrings.English);
            var window = new Window
            {
                Width = width, Height = 240, WindowStyle = WindowStyle.None, Content = visual.Toolbar.Layer,
            };
            try
            {
                window.Show();
                visual.Toolbar.Show(new Rect(width - 60, 180, 60, 40), new Size(width, 240));
                visual.Toolbar.SetPromptOpen(true);
                window.UpdateLayout();

                Assert.Same(visual.AskPrompt.Root, visual.Toolbar.Surface.Child);
                Assert.True(visual.AskPrompt.Input.IsKeyboardFocused);
                var left = Canvas.GetLeft(visual.Toolbar.Surface);
                Assert.InRange(left, 0, width);
                Assert.InRange(left + visual.Toolbar.Surface.ActualWidth, 0, width + 0.01);
                Assert.InRange(visual.Toolbar.Surface.ActualWidth, Math.Min(380, width - 12), width);
                Assert.Equal(44, visual.Toolbar.Surface.ActualHeight);
                var send = visual.AskPrompt.SendButton;
                var bounds = send.TransformToAncestor(visual.Toolbar.Layer).TransformBounds(new Rect(send.RenderSize));
                Assert.InRange(bounds.Right, 0, width + 0.01);

                visual.Toolbar.SetPromptOpen(false);
                window.UpdateLayout();
                Assert.False(visual.Toolbar.IsPromptOpen);
                Assert.IsType<WrapPanel>(visual.Toolbar.Surface.Child);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Updating_search_icon_keeps_open_toolbar_inside_viewport_and_preserves_button(bool lightTheme)
    {
        RunOnSta(time =>
        {
            var toolbar = new FloatingToolbar(PluginPalette.For(lightTheme).FloatingToolbar, () => false);
            var search = toolbar.AddAction(TestUiStrings.English.TextSearch);
            var clicks = 0;
            search.Click += (_, _) => clicks++;
            var window = new Window { Width = 320, Height = 200, Content = toolbar.Layer };
            try
            {
                window.Show();
                toolbar.Show(new Rect(290, 100, 30, 20), new Size(320, 200));
                window.UpdateLayout();
                var initialWidth = toolbar.Surface.ActualWidth;
                foreach (var provider in new[] { SearchProviderIds.GoogleLens, SearchProviderIds.YandexImages, SearchProviderIds.TraceMoe })
                {
                    var icon = ProviderVisualCatalog.CreateTextSearchMark(provider, TextSearchEngines.MatchImageSearch, lightTheme);
                    toolbar.SetActionContent(search, TestUiStrings.English.TextSearch, icon);
                    window.UpdateLayout();
                    Assert.True(toolbar.IsOpen);
                    Assert.True(toolbar.Surface.ActualWidth > initialWidth);
                    Assert.InRange(Canvas.GetLeft(toolbar.Surface) + toolbar.Surface.ActualWidth, 0, 320);
                    Assert.Equal(44, toolbar.Surface.ActualHeight);
                    var bounds = search.TransformToAncestor(toolbar.Surface).TransformBounds(new Rect(search.RenderSize));
                    Assert.Equal(bounds.Top, toolbar.Surface.ActualHeight - bounds.Bottom, 6);
                    Assert.True(search.Padding.Left < search.Padding.Right);
                    var row = Assert.IsType<StackPanel>(search.Content);
                    Assert.Same(icon, Assert.IsType<ContentControl>(row.Children[0]).Content);
                    Assert.Equal(TestUiStrings.English.TextSearch, Assert.IsType<TextBlock>(row.Children[1]).Text);
                    search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (provider == SearchProviderIds.TraceMoe)
                    {
                        var drawing = Assert.IsType<DrawingGroup>(Assert.IsType<DrawingImage>(Assert.IsType<Image>(icon).Source).Drawing);
                        Assert.Equal(new Rect(0, 0, 24, 24), drawing.Bounds);
                        Assert.Same(PluginIcons.AniListBlue, Assert.IsType<GeometryDrawing>(drawing.Children[1]).Geometry);
                        var imageMark = Assert.IsType<Image>(ProviderVisualCatalog.CreateSearchMark(provider, lightTheme));
                        var imageDrawing = Assert.IsType<DrawingGroup>(Assert.IsType<DrawingImage>(imageMark.Source).Drawing);
                        Assert.Same(PluginIcons.TraceMoe, Assert.IsType<GeometryDrawing>(imageDrawing.Children[1]).Geometry);
                    }
                }
                Assert.Equal(3, clicks);
                toolbar.SetActionContent(search, TestUiStrings.English.TextSearch);
                window.UpdateLayout();
                Assert.Equal(initialWidth, toolbar.Surface.ActualWidth);
                Assert.Equal(TestUiStrings.English.TextSearch, search.Content);
                Assert.Equal(search.Padding.Left, search.Padding.Right);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Hidden_toolbar_stops_input_immediately_and_reopening_cancels_pending_exit()
    {
        RunOnSta(time =>
        {
            var toolbar = new FloatingToolbar(PluginPalette.For(false).FloatingToolbar, () => true);
            var copy = toolbar.AddAction(TestUiStrings.English.TextCopy);
            var window = new Window { Width = 400, Height = 200, Content = toolbar.Layer };
            try
            {
                window.Show();
                toolbar.Show(new Rect(120, 100, 80, 20), new Size(400, 200));
                time.Advance(280);
                Assert.Equal(1, toolbar.Surface.Opacity, 3);

                toolbar.Hide();
                Assert.False(toolbar.IsOpen);
                Assert.False(copy.IsEnabled);
                Assert.False(toolbar.Surface.IsHitTestVisible);
                Assert.Equal(Visibility.Visible, toolbar.Surface.Visibility);
                time.Advance(45);
                var opacity = toolbar.Surface.Opacity;
                toolbar.Show(new Rect(120, 100, 80, 20), new Size(400, 200));
                time.Advance(20);
                Assert.InRange(toolbar.Surface.Opacity, opacity, 0.999);
                time.Advance(300);

                Assert.True(toolbar.IsOpen);
                Assert.True(copy.IsEnabled);
                Assert.True(toolbar.Surface.IsHitTestVisible);
                Assert.Equal(Visibility.Visible, toolbar.Surface.Visibility);
                Assert.Equal(1, toolbar.Surface.Opacity, 3);
                toolbar.Hide();
                toolbar.Hide();
                time.Advance(200);
                Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);
                Assert.False(toolbar.Surface.HasAnimatedProperties);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Disabled_animation_policy_and_immediate_hide_leave_no_animation_clocks()
    {
        RunOnSta(time =>
        {
            var enabled = false;
            var toolbar = new FloatingToolbar(PluginPalette.For(true).FloatingToolbar, () => enabled);
            toolbar.AddAction(TestUiStrings.English.TextCopy);
            toolbar.Show(new Rect(100, 100, 40, 20), new Size(400, 200));
            Assert.Equal(1, toolbar.Surface.Opacity);
            Assert.False(toolbar.Surface.HasAnimatedProperties);
            toolbar.Hide();
            Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);

            enabled = true;
            toolbar.Show(new Rect(100, 100, 40, 20), new Size(400, 200));
            toolbar.Hide(animate: false);
            var transforms = CardTransitions.GetTransforms(toolbar.Surface);
            Assert.False(toolbar.IsOpen);
            Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);
            Assert.False(toolbar.Surface.HasAnimatedProperties);
            Assert.False(transforms.Scale.HasAnimatedProperties);
            Assert.False(transforms.Translate.HasAnimatedProperties);
        });
    }

    [Fact]
    public void Unloading_during_exit_cancels_animations_and_collapses_toolbar()
    {
        RunOnSta(time =>
        {
            var toolbar = new FloatingToolbar(PluginPalette.For(false).FloatingToolbar, () => true);
            toolbar.AddAction(TestUiStrings.English.TextCopy);
            toolbar.Show(new Rect(100, 100, 40, 20), new Size(400, 200));
            toolbar.Hide();
            toolbar.Surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.False(toolbar.IsOpen);
            Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);
            Assert.False(toolbar.Surface.HasAnimatedProperties);
            time.Advance(240);
            Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Text_search_mark_follows_the_selected_engine_and_falls_back_to_the_image_provider(bool lightTheme)
    {
        RunOnSta(_ =>
        {
            var marks = new Dictionary<string, Geometry>
            {
                [TextSearchEngines.Bing] = PluginIcons.BingUpper,
                [TextSearchEngines.DuckDuckGo] = PluginIcons.DuckDuckGoDisc,
                [TextSearchEngines.Google] = PluginIcons.GoogleRed,
                [TextSearchEngines.Kagi] = PluginIcons.KagiHandle,
                [TextSearchEngines.Qwant] = PluginIcons.QwantMark,
                [TextSearchEngines.Startpage] = PluginIcons.StartpageMark,
            };
            Assert.Equal(TextSearchEngines.All.Select(engine => engine.Id), marks.Keys);
            foreach (var (engine, geometry) in marks)
            {
                var layer = FirstMarkLayer(ProviderVisualCatalog.CreateTextSearchMark(SearchProviderIds.TraceMoe, engine, lightTheme));
                Assert.Same(geometry, layer.Geometry);
            }
            var qwant = FirstMarkLayer(ProviderVisualCatalog.CreateTextSearchMark(
                SearchProviderIds.GoogleLens, TextSearchEngines.Qwant, lightTheme));
            Assert.Equal(PluginPalette.For(lightTheme).Provider.Qwant, Assert.IsType<SolidColorBrush>(qwant.Brush).Color);
            Assert.Same(PluginIcons.AniListBlue, FirstMarkLayer(ProviderVisualCatalog.CreateTextSearchMark(
                SearchProviderIds.TraceMoe, TextSearchEngines.MatchImageSearch, lightTheme)).Geometry);
            Assert.Same(PluginIcons.YandexLetter, FirstMarkLayer(ProviderVisualCatalog.CreateTextSearchMark(
                SearchProviderIds.YandexImages, TextSearchEngines.MatchImageSearch, lightTheme)).Geometry);
        });
    }

    private static GeometryDrawing FirstMarkLayer(FrameworkElement mark)
    {
        var drawing = Assert.IsType<DrawingGroup>(Assert.IsType<DrawingImage>(Assert.IsType<Image>(mark).Source).Drawing);
        Assert.Equal(0, drawing.Bounds.Left, 3);
        Assert.Equal(0, drawing.Bounds.Top, 3);
        Assert.Equal(24, drawing.Bounds.Right, 3);
        Assert.Equal(24, drawing.Bounds.Bottom, 3);
        return Assert.IsType<GeometryDrawing>(drawing.Children[1]);
    }

    private static void RunOnSta(Action<ManualAnimationClock> action)
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
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
