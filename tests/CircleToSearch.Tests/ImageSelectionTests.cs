using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Search;
using CircleToSearch.Translation;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class ImageSelectionTests
{
    [Fact]
    public void Renders_image_action_previews()
    {
        if (Environment.GetEnvironmentVariable("CTS_IMAGE_SELECTION_PREVIEW") != "1") return;
        Run(() =>
        {
            using var h = new Harness();
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(Brushes.LightSlateGray, null, new Rect(0, 0, 640, 400));
                context.DrawRoundedRectangle(Brushes.WhiteSmoke, null, new Rect(32, 32, 576, 336), 12, 12);
                context.DrawText(new FormattedText("A sample document", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 24, Brushes.Black, 1), new Point(64, 62));
                context.DrawText(new FormattedText("Select this image to search, copy, save or translate.",
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 17, Brushes.DimGray, 1), new Point(64, 120));
                context.DrawRectangle(Brushes.LightSteelBlue, null, new Rect(64, 175, 220, 145));
                context.DrawRectangle(Brushes.Gainsboro, null, new Rect(308, 175, 260, 145));
            }
            var source = new RenderTargetBitmap(640, 400, 96, 96, PixelFormats.Pbgra32);
            source.Render(drawing);
            source.Freeze();
            h.Window.VisualState.Selection.Screenshot.Source = source;
            h.Select(new Point(54, 106), new Point(575, 150));
            Pump(TimeSpan.FromMilliseconds(300));
            Capture(h, "image-selection-actions.png");
            Click(h.Actions.TranslateButton);
            var request = h.Commands.OfType<ScreenTranslationRequested>().Last();
            h.Window.ShowTranslation(new ScreenTranslationResult(request.RequestId, request.Image));
            Pump(TimeSpan.FromMilliseconds(300));
            Capture(h, "image-selection-show-original.png");
        });
    }

    private static void Capture(Harness h, string name)
    {
        var root = h.Window.VisualState.Root;
        root.UpdateLayout();
        var output = new RenderTargetBitmap((int)(root.ActualWidth * 1.5), (int)(root.ActualHeight * 1.5),
            144, 144, PixelFormats.Pbgra32);
        output.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        Directory.CreateDirectory(TestOutputPaths.TempDirectory);
        using var file = File.Create(Path.Combine(TestOutputPaths.TempDirectory, name));
        encoder.Save(file);
    }

    [Fact]
    public void Right_click_does_nothing_and_drag_keeps_frame_with_five_actions() => Run(() =>
    {
        using var h = new Harness();
        h.RightClick();
        Assert.Equal(OverlayInteractionMode.Selecting, h.Window.Mode);
        Assert.Empty(h.Commands);
        Assert.False(h.Actions.Toolbar.IsOpen);
        h.Select();
        Assert.Empty(h.Commands);
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.False(h.Window.VisualState.Selection.SelectionFrame.Data.IsEmpty());
        Assert.Equal(new[] { "Search", "Copy", "Save", "Translate", "Ask" },
            new[] { h.Actions.SearchButton, h.Actions.CopyButton, h.Actions.SaveButton, h.Actions.TranslateButton,
                    h.Actions.AskButton }
                .Select(AutomationProperties.GetName));
        h.RightClick();
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.Empty(h.Commands);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ask_submits_trimmed_question_with_visible_pixels(bool pressEnter) => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        h.Translate();
        var before = h.Commands.Count;
        Click(h.Actions.AskButton);
        var prompt = h.Actions.AskPrompt;
        Assert.True(h.Actions.Toolbar.IsPromptOpen);
        Assert.Same(prompt.Root, h.Actions.Toolbar.Surface.Child);
        Assert.False(prompt.SendButton.IsEnabled);
        Assert.IsType<AskDraftStarted>(Assert.Single(h.Commands.Skip(before)));

        prompt.Input.Text = " ";
        prompt.Input.Text = "  What is red?  ";
        var attached = Assert.Single(h.Commands.OfType<AskImageAttached>());
        Assert.Equal(255, attached.Selection.FrozenFrame.GetPixel(attached.Selection.Bounds.X,
            attached.Selection.Bounds.Y).R);
        Assert.True(prompt.SendButton.IsEnabled);
        if (pressEnter) h.Key(prompt.Input, Key.Enter, Keyboard.KeyDownEvent);
        else Click(prompt.SendButton);

        var asked = Assert.Single(h.Commands.OfType<AskAboutSelection>());
        Assert.Equal("What is red?", asked.Question);
        Assert.Null(asked.Selection);
        Assert.False(h.Actions.Toolbar.IsOpen);
        Click(prompt.SendButton);
        Assert.Single(h.Commands.OfType<AskAboutSelection>());
        Assert.Empty(h.Commands.OfType<AskDraftCanceled>());
    });

    [Fact]
    public void Escape_returns_from_ask_prompt_to_actions_and_blank_question_is_ignored() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        Click(h.Actions.AskButton);
        var prompt = h.Actions.AskPrompt;
        prompt.Input.Text = "   ";
        h.Key(prompt.Input, Key.Enter, Keyboard.KeyDownEvent);
        Assert.Empty(h.Commands.OfType<AskAboutSelection>());

        h.Escape();

        Assert.False(h.Actions.Toolbar.IsPromptOpen);
        Assert.Empty(prompt.Input.Text);
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.Equal(OverlayInteractionMode.Selecting, h.Window.Mode);
        Assert.Equal([typeof(AskDraftStarted), typeof(AskImageAttached), typeof(AskDraftCanceled)],
            h.Commands.Select(command => command.GetType()));
        h.Escape();
        Assert.Single(h.Commands.OfType<CancelSession>());
    });

    [Fact]
    public void Ask_without_typing_never_attaches_the_image() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        Click(h.Actions.AskButton);
        h.Select(new Point(20, 20), new Point(45, 45));
        Assert.Equal([typeof(AskDraftStarted), typeof(AskDraftCanceled)],
            h.Commands.Select(command => command.GetType()));
        Click(h.Actions.AskButton);
        h.Actions.AskPrompt.Input.Text = "a";
        h.Window.CloseFromSession();
        Assert.Single(h.Commands.OfType<AskImageAttached>());
        Assert.Equal(2, h.Commands.OfType<AskDraftCanceled>().Count());
    });

    [Fact]
    public void New_selection_resets_ask_prompt() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        Click(h.Actions.AskButton);
        h.Actions.AskPrompt.Input.Text = "draft";
        h.Select(new Point(20, 20), new Point(45, 45));
        Assert.False(h.Actions.Toolbar.IsPromptOpen);
        Assert.Empty(h.Actions.AskPrompt.Input.Text);
        Assert.True(h.Actions.Toolbar.IsOpen);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copy_uses_visible_crop_and_closes_only_after_feedback(bool original) => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        h.Translate();
        if (original) Click(h.Actions.TranslateButton);
        Click(h.Actions.CopyButton);
        Assert.NotNull(h.Copied);
        Assert.Equal(h.Request!.Image.PixelWidth, h.Copied!.PixelWidth);
        Assert.Equal(original ? (byte)0 : (byte)255, Pixel(h.Copied, 0, 0)[2]);
        Assert.DoesNotContain(h.Commands, command => command is CancelSession);
        Assert.False(h.Actions.Toolbar.Surface.IsEnabled);
        var toast = Assert.IsType<Grid>(h.Window.VisualState.Bottom.Stack.Children[0]);
        Assert.Equal(TestUiStrings.English.ImageCopied,
            Assert.IsType<TextBlock>(Assert.IsType<Border>(toast.Children[0]).Child).Text);
        Pump(TimeSpan.FromMilliseconds(300));
        Assert.DoesNotContain(h.Commands, command => command is CancelSession);
        Pump(TimeSpan.FromMilliseconds(650));
        Assert.Single(h.Commands.OfType<CancelSession>());
    });

    [Fact]
    public void Failed_copy_keeps_toolbar_and_selection_available() => Run(() =>
    {
        using var h = new Harness(copyFails: true);
        h.Select();
        Click(h.Actions.CopyButton);
        Assert.True(h.Actions.Toolbar.Surface.IsEnabled);
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.Empty(h.Commands);
        Click(h.Actions.SaveButton);
        Assert.IsType<SaveSelectedImage>(Assert.Single(h.Commands));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_captures_visible_pixels_in_a_frozen_image(bool original) => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        h.Translate();
        if (original) Click(h.Actions.TranslateButton);
        Click(h.Actions.SaveButton);
        var saved = Assert.Single(h.Commands.OfType<SaveSelectedImage>());
        Assert.True(saved.Image.IsFrozen);
        Assert.Equal(original ? (byte)0 : (byte)255, Pixel(saved.Image, 0, 0)[2]);
        Click(h.Actions.SaveButton);
        Assert.Single(h.Commands.OfType<SaveSelectedImage>());
    });

    [Theory]
    [InlineData(SearchProviderIds.GoogleLens, false)]
    [InlineData(SearchProviderIds.GoogleLens, true)]
    [InlineData(SearchProviderIds.YandexImages, false)]
    [InlineData(SearchProviderIds.TraceMoe, false)]
    public void Search_uses_selected_provider_and_visible_pixels(string provider, bool original) => Run(() =>
    {
        using var h = new Harness(provider: provider);
        h.Select();
        h.Translate();
        if (original) Click(h.Actions.TranslateButton);
        Click(h.Actions.SearchButton);
        var searched = Assert.Single(h.Commands.OfType<VisualSelection>());
        Assert.Equal(provider, searched.ProviderId);
        var bounds = searched.Selection.Bounds;
        Assert.Equal(original ? 0 : 255, searched.Selection.FrozenFrame.GetPixel(bounds.X, bounds.Y).R);
        Assert.Equal(provider == SearchProviderIds.TraceMoe ? OverlayInteractionMode.TraceLoading : OverlayInteractionMode.Closing,
            h.Window.Mode);
    });

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.5, true)]
    [InlineData(2, true)]
    public void Translation_changes_only_selected_pixels_and_new_drag_replaces_scope(double scale, bool overscan) => Run(() =>
    {
        using var h = new Harness(scale: scale, overscan: overscan);
        h.Select();
        h.Translate();
        var request = h.Request!;
        Assert.True(request.Image.PixelWidth < 640);
        Assert.True(request.Image.PixelHeight < 400);
        var displayed = (BitmapSource)h.Window.VisualState.Selection.Screenshot.Source;
        var inset = overscan ? 1 : 0;
        var x = (int)Math.Round((50 - inset) * scale);
        var y = (int)Math.Round((50 - inset) * scale);
        Assert.Equal(255, Pixel(displayed, x, y)[2]);
        Assert.Equal(0, Pixel(displayed, x - 1, y)[2]);
        Assert.Equal(0, Pixel(displayed, 639, 399)[2]);
        Assert.Equal(TestUiStrings.English.ShowOriginal, h.Actions.TranslateButton.Content);
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.False(h.Window.VisualState.Selection.SelectionFrame.Data.IsEmpty());
        h.Select(new Point(20, 20), new Point(45, 45));
        Assert.Equal(OverlayInteractionMode.Selecting, h.Window.Mode);
        Assert.Equal(TestUiStrings.English.Translate, h.Actions.TranslateButton.Content);
        Click(h.Actions.TranslateButton);
        var second = h.Commands.OfType<ScreenTranslationRequested>().Last();
        Assert.NotEqual(request.RequestId, second.RequestId);
        Assert.True(second.Image.PixelWidth < request.Image.PixelWidth);
        h.Window.ShowTranslation(new ScreenTranslationResult(request.RequestId, Solid(10, 10, Colors.Blue)));
        Assert.Equal(OverlayInteractionMode.Translating, h.Window.Mode);
    });

    [Fact]
    public void Escape_during_trace_search_after_translation_closes_without_invalid_transition() => Run(() =>
    {
        using var h = new Harness(provider: SearchProviderIds.TraceMoe);
        h.Select();
        h.Translate();
        Click(h.Actions.SearchButton);
        Assert.Equal(OverlayInteractionMode.TraceLoading, h.Window.Mode);

        h.Escape();

        Assert.Equal(OverlayInteractionMode.Closing, h.Window.Mode);
        Assert.Single(h.Commands.OfType<CancelSession>());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closing_trace_result_restores_fullscreen_translation_and_keeps_visible_pixels(bool translated) => Run(() =>
    {
        using var h = new Harness(provider: SearchProviderIds.TraceMoe);
        h.Select();
        if (translated) h.Translate();
        var visible = (BitmapSource)h.Window.VisualState.Selection.Screenshot.Source;
        Click(h.Actions.SearchButton);
        Assert.Same(visible, h.Window.VisualState.Selection.Screenshot.Source);
        h.Window.ShowTraceResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(null)));
        Pump(TimeSpan.FromMilliseconds(450));
        var close = Descendants(h.Window.VisualState.Bottom.Stack).OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close);
        Click(close);
        Assert.Equal(OverlayInteractionMode.Selecting, h.Window.Mode);
        Assert.False(h.Actions.Toolbar.IsOpen);

        var previousRequests = h.Commands.OfType<ScreenTranslationRequested>().Count();
        Click(h.Window.VisualState.TranslationAction.Button);

        Assert.Equal(previousRequests + 1, h.Commands.OfType<ScreenTranslationRequested>().Count());
        var request = h.Commands.OfType<ScreenTranslationRequested>().Last();
        Assert.Equal(640, request.Image.PixelWidth);
        Assert.Equal(400, request.Image.PixelHeight);
        Assert.Same(visible, request.Image);
        Assert.Equal(translated ? (byte)255 : (byte)0, Pixel(request.Image, 60, 60)[2]);
        Assert.Equal(0, Pixel(request.Image, 0, 0)[2]);
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    [Fact]
    public void Regional_translation_preserves_consent_and_recovers_from_failure() => Run(() =>
    {
        using var h = new Harness(consent: false);
        h.Select();
        Click(h.Actions.TranslateButton);
        Assert.Equal(OverlayInteractionMode.TranslationConsent, h.Window.Mode);
        Assert.Empty(h.Commands);
        h.Escape();
        Assert.True(h.Actions.Toolbar.IsOpen);
        h.Consent = true;
        Click(h.Actions.TranslateButton);
        var request = Assert.Single(h.Commands.OfType<ScreenTranslationRequested>());
        h.Window.ShowTranslationFailure(request.RequestId, TranslationFailure.Network);
        Assert.Equal(OverlayInteractionMode.TranslationResult, h.Window.Mode);
        h.Escape();
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.False(h.Window.VisualState.Selection.SelectionFrame.Data.IsEmpty());
        Click(h.Actions.TranslateButton);
        Assert.Equal(2, h.Commands.OfType<ScreenTranslationRequested>().Count());
    });

    [Fact]
    public void Clicking_background_while_translating_does_not_reset_region() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        Click(h.Actions.TranslateButton);
        var request = Assert.Single(h.Commands.OfType<ScreenTranslationRequested>());
        var input = h.Window.VisualState.Selection.InputSurface;
        input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = input });
        h.Window.ShowTranslation(new ScreenTranslationResult(request.RequestId, Solid(20, 20, Colors.Red)));
        var displayed = (BitmapSource)h.Window.VisualState.Selection.Screenshot.Source;
        Assert.Equal(0, Pixel(displayed, 0, 0)[2]);
        Assert.Equal(255, Pixel(displayed, 60, 60)[2]);
        Assert.True(h.Actions.Toolbar.IsOpen);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encoders_write_requested_format_at_original_resolution(bool png)
    {
        var source = Solid(200, 100, Colors.Red);
        using var stream = new MemoryStream();
        ImageSaveService.Encode(source, stream, png);
        stream.Position = 0;
        var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(png, decoded is PngBitmapDecoder);
        Assert.Equal(!png, decoded is JpegBitmapDecoder);
        Assert.Equal(200, decoded.Frames[0].PixelWidth);
        Assert.Equal(100, decoded.Frames[0].PixelHeight);
    }

    private static byte[] Pixel(BitmapSource image, int x, int y)
    {
        var formatted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        formatted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel;
    }

    private static BitmapSource Solid(int width, int height, Color color)
    {
        var pixels = Enumerable.Range(0, width * height).SelectMany(_ => new[] { color.B, color.G, color.R, color.A }).ToArray();
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(error);
    }

    private sealed class Harness : IDisposable
    {
        private readonly GdiBitmap _frame = new(640, 400);
        private Point _point;
        internal bool Consent;
        internal List<IOverlayCommand> Commands { get; } = [];
        internal OverlayWindow Window { get; }
        internal ImageSelectionVisual Actions => Window.VisualState.ImageSelection;
        internal BitmapSource? Copied;
        internal ScreenTranslationRequested? Request;

        internal Harness(string provider = SearchProviderIds.GoogleLens, bool copyFails = false,
            double scale = 1, bool overscan = false, bool consent = true)
        {
            Consent = consent;
            using (var graphics = System.Drawing.Graphics.FromImage(_frame)) graphics.Clear(System.Drawing.Color.Black);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            Window = new OverlayWindow(_frame, monitor, monitor, scale,
                new OverlayLaunchOptions(new OverlayOptions(0, 12), TestUiStrings.English,
                    [new(provider, provider)], provider), Commands.Add,
                TestOverlayControllers.CreateFactory(animationsEnabled: () => false, pointerPosition: _ => _point,
                    translationConsentAccepted: () => Consent,
                    setImageClipboard: image =>
                    {
                        if (copyFails) throw new InvalidOperationException("clipboard busy");
                        Copied = image;
                    }), overscan: overscan);
            Window.Show();
            Window.UpdateLayout();
        }

        internal void Select(Point? start = null, Point? end = null)
        {
            _point = start ?? new Point(50, 50);
            Raise(UIElement.MouseRightButtonDownEvent);
            _point = end ?? new Point(150, 120);
            var input = Window.VisualState.Selection.InputSurface;
            input.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent, Source = input });
            Raise(UIElement.MouseRightButtonUpEvent);
            Window.UpdateLayout();
        }

        internal void RightClick()
        {
            _point = new Point(10, 10);
            Raise(UIElement.MouseRightButtonDownEvent);
            Raise(UIElement.MouseRightButtonUpEvent);
        }

        internal void Translate()
        {
            Click(Actions.TranslateButton);
            Request = Commands.OfType<ScreenTranslationRequested>().Last();
            Window.ShowTranslation(new ScreenTranslationResult(Request.RequestId, Solid(30, 20, Colors.Red)));
            Window.UpdateLayout();
        }

        internal void Escape() => Key(Window, System.Windows.Input.Key.Escape, Keyboard.PreviewKeyDownEvent);

        internal void Key(UIElement target, Key key, RoutedEvent routedEvent) =>
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window)!, 0, key)
            { RoutedEvent = routedEvent });

        private void Raise(RoutedEvent routedEvent)
        {
            var input = Window.VisualState.Selection.InputSurface;
            input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
            { RoutedEvent = routedEvent, Source = input });
        }

        public void Dispose()
        {
            Window.CloseFromSession();
            foreach (var command in Commands) OverlayCommandOwnership.DisposePayload(command);
            _frame.Dispose();
        }
    }
}
