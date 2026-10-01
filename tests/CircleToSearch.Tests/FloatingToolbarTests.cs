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
                    visual.Toolbar.SetActionLabel(visual.TranslateButton, label);
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
                Assert.All([.. buttons, visual.AskButton],
                    button => Assert.Equal(new Thickness(10, 0, 12, 0), button.Padding));
                Assert.Equal(42, visual.Toolbar.Surface.ActualHeight);
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
                var available = FloatingToolbarLayout.AvailableWidth(width, default);
                Assert.InRange(left, 16, width);
                Assert.InRange(left + visual.Toolbar.Surface.ActualWidth, 0, width - 16 + 0.01);
                Assert.InRange(visual.Toolbar.Surface.ActualWidth, Math.Min(380, available - 12), available);
                Assert.Equal(42, visual.Toolbar.Surface.ActualHeight);
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
    public void Ask_prompt_shows_keyboard_layout_tag_and_fills_send_once_text_is_entered(bool lightTheme)
    {
        RunOnSta(_ =>
        {
            var palette = PluginPalette.For(lightTheme).FloatingToolbar;
            var visual = ImageSelectionVisualFactory.Create(lightTheme, TestUiStrings.English);
            var prompt = visual.AskPrompt;
            var window = new Window
            {
                Width = 640, Height = 240, WindowStyle = WindowStyle.None, Content = visual.Toolbar.Layer,
            };
            try
            {
                window.Show();
                visual.Toolbar.Show(new Rect(100, 180, 60, 40), new Size(640, 240));
                visual.Toolbar.SetPromptOpen(true);
                Assert.Equal(Visibility.Collapsed, prompt.LanguageTag.Root.Visibility);

                visual.Toolbar.SetPromptLanguage("ru-RU");
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, prompt.LanguageTag.Root.Visibility);
                Assert.Equal("RU", prompt.LanguageTag.Label);
                var tag = prompt.LanguageTag.Root.TransformToAncestor(visual.Toolbar.Layer)
                    .TransformBounds(new Rect(prompt.LanguageTag.Root.RenderSize));
                var send = prompt.SendButton.TransformToAncestor(visual.Toolbar.Layer)
                    .TransformBounds(new Rect(prompt.SendButton.RenderSize));
                Assert.True(tag.Right <= send.Left);
                Assert.Equal(42, visual.Toolbar.Surface.ActualHeight);
                Assert.False(prompt.SendButton.IsEnabled);

                prompt.Input.Text = "Что это за здание?";
                Assert.True(prompt.SendButton.IsEnabled);
                Assert.Equal(palette.Accent, Assert.IsType<SolidColorBrush>(prompt.SendButton.Background).Color);
                Assert.Equal(palette.OnAccent, Assert.IsType<SolidColorBrush>(prompt.SendButton.Foreground).Color);

                prompt.Input.Clear();
                Assert.Equal(PluginPalette.Transparent, Assert.IsType<SolidColorBrush>(prompt.SendButton.Background).Color);
                visual.Toolbar.SetPromptLanguage(null);
                Assert.Equal(Visibility.Collapsed, prompt.LanguageTag.Root.Visibility);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Keyboard_layout_switch_rolls_the_tag_to_the_new_code_and_settles()
    {
        RunOnSta(time =>
        {
            var palette = PluginPalette.For(false).FloatingToolbar;
            var toolbar = new FloatingToolbar(palette, () => true);
            var prompt = toolbar.AddPrompt(TestUiStrings.English.AskPlaceholder, TestUiStrings.English.AskSend,
                PluginIcons.SparkleOutlined);
            var tag = prompt.LanguageTag;
            toolbar.SetPromptLanguage("en-US");
            Assert.Equal(1, tag.Root.Opacity);
            var window = new Window { Width = 640, Height = 240, WindowStyle = WindowStyle.None, Content = toolbar.Layer };
            try
            {
                window.Show();
                toolbar.Show(new Rect(100, 180, 60, 40), new Size(640, 240));
                toolbar.SetPromptOpen(true);
                time.Advance(260);

                toolbar.SetPromptLanguage("ru-RU");
                time.Advance(100);
                Assert.Equal("RU", tag.Label);
                Assert.Equal("EN", tag.OutgoingLabel);
                var scale = (ScaleTransform)tag.Root.RenderTransform;
                Assert.True(scale.ScaleX > 1);
                Assert.NotEqual(palette.Tag, ((SolidColorBrush)tag.Root.Background).Color);

                time.Advance(400);
                Assert.Equal("RU", tag.Label);
                Assert.Equal(string.Empty, tag.OutgoingLabel);
                Assert.Equal(1, scale.ScaleX, 3);
                Assert.Equal(palette.Tag, ((SolidColorBrush)tag.Root.Background).Color);

                toolbar.SetPromptLanguage(null);
                Assert.Equal(Visibility.Collapsed, tag.Root.Visibility);
                toolbar.SetPromptLanguage("en-US");
                time.Advance(60);
                Assert.InRange(tag.Root.Opacity, 0.01, 0.99);
                time.Advance(240);
                Assert.Equal(1, tag.Root.Opacity, 3);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Tag_width_follows_a_shorter_code_smoothly_and_does_not_jump_when_the_roll_ends()
    {
        RunOnSta(time =>
        {
            var toolbar = new FloatingToolbar(PluginPalette.For(false).FloatingToolbar, () => true);
            var prompt = toolbar.AddPrompt(TestUiStrings.English.AskPlaceholder, TestUiStrings.English.AskSend,
                PluginIcons.SparkleOutlined);
            var tag = prompt.LanguageTag;
            toolbar.SetPromptLanguage("haw-US");
            var window = new Window { Width = 640, Height = 240, WindowStyle = WindowStyle.None, Content = toolbar.Layer };
            try
            {
                window.Show();
                toolbar.Show(new Rect(100, 180, 60, 40), new Size(640, 240));
                toolbar.SetPromptOpen(true);
                time.Advance(260);
                window.UpdateLayout();
                Assert.Equal("HAW", tag.Label);
                var wide = tag.Root.ActualWidth;

                toolbar.SetPromptLanguage("en-US");
                var widths = new List<double>();
                for (var elapsed = 0; elapsed < 256; elapsed += 16)
                {
                    time.Advance(16);
                    window.UpdateLayout();
                    widths.Add(tag.Root.ActualWidth);
                }
                time.Advance(200);
                window.UpdateLayout();
                var settled = tag.Root.ActualWidth;

                Assert.Equal(string.Empty, tag.OutgoingLabel);
                Assert.True(settled < wide - 4);
                Assert.Contains(widths, width => width < wide - 1 && width > settled + 1);
                Assert.All(widths.Zip(widths.Skip(1)), step => Assert.InRange(step.First - step.Second, -0.01, (wide - settled) / 2));
                Assert.InRange(widths[^1] - settled, -0.5, 0.5);
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
                    Assert.Equal(42, toolbar.Surface.ActualHeight);
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
    public void Divider_collapses_when_no_visible_action_remains_on_one_side()
    {
        RunOnSta(_ =>
        {
            var toolbar = new FloatingToolbar(PluginPalette.For(false).FloatingToolbar, () => false);
            var search = toolbar.AddAction(TestUiStrings.English.TextSearch);
            toolbar.AddDivider();
            var copy = toolbar.AddAction(TestUiStrings.English.TextCopy, PluginIcons.CopyOutlined);
            var save = toolbar.AddAction(TestUiStrings.English.ImageSave, PluginIcons.DownloadOutlined);
            var divider = Assert.IsType<WrapPanel>(toolbar.Surface.Child).Children.OfType<Border>().Single();
            var anchor = new Rect(100, 100, 40, 20);
            var viewport = new Size(400, 200);

            toolbar.Show(anchor, viewport);
            Assert.Equal(Visibility.Visible, divider.Visibility);
            copy.Visibility = Visibility.Collapsed;
            toolbar.Show(anchor, viewport);
            Assert.Equal(Visibility.Visible, divider.Visibility);
            save.Visibility = Visibility.Collapsed;
            toolbar.Show(anchor, viewport);
            Assert.Equal(Visibility.Collapsed, divider.Visibility);
            save.Visibility = Visibility.Visible;
            search.Visibility = Visibility.Collapsed;
            toolbar.Show(anchor, viewport);
            Assert.Equal(Visibility.Collapsed, divider.Visibility);
        });
    }

    [Fact]
    public void Changing_an_action_label_keeps_its_icon()
    {
        RunOnSta(_ =>
        {
            var toolbar = new FloatingToolbar(PluginPalette.For(false).FloatingToolbar, () => false);
            var translate = toolbar.AddAction(TestUiStrings.English.Translate, PluginIcons.TranslateOutlined);
            var icon = Assert.IsType<ContentControl>(Assert.IsType<StackPanel>(translate.Content).Children[0]).Content;

            toolbar.SetActionLabel(translate, TestUiStrings.English.ShowOriginal);

            var row = Assert.IsType<StackPanel>(translate.Content);
            Assert.Same(icon, Assert.IsType<ContentControl>(row.Children[0]).Content);
            Assert.Equal(TestUiStrings.English.ShowOriginal, Assert.IsType<TextBlock>(row.Children[1]).Text);
            Assert.Equal(TestUiStrings.English.ShowOriginal, System.Windows.Automation.AutomationProperties.GetName(translate));
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
