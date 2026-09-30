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
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class ImageSelectionTests
{
    // Far from the default selection and its toolbar.
    private static readonly Point EmptyPoint = new(600, 380);

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
            DispatcherPump.For(300);
            Capture(h, "image-selection-actions.png");
            Click(h.Actions.TranslateButton);
            var request = h.Commands.OfType<ScreenTranslationRequested>().Last();
            h.Window.ShowTranslation(new ScreenTranslationResult(request.RequestId, request.Image));
            DispatcherPump.For(300);
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
    public void Right_click_is_rejected_as_too_small_and_drag_keeps_frame_with_five_actions() => Run(() =>
    {
        using var h = new Harness();
        h.RightClick();
        Assert.Equal(OverlayInteractionMode.Selecting, h.Window.Mode);
        Assert.Empty(h.Commands);
        Assert.False(h.Actions.Toolbar.IsOpen);
        Assert.False(h.Window.VisualState.Selection.InputSurface.IsMouseCaptured);
        Assert.Equal(TestUiStrings.English.SelectionTooSmall, h.ToastText());
        h.Select();
        Assert.Empty(h.Commands);
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.False(h.Window.VisualState.Selection.SelectionFrame.Data.IsEmpty());
        Assert.Equal(new[] { "Search", "Copy", "Save", "Translate", "Ask" },
            new[] { h.Actions.SearchButton, h.Actions.CopyButton, h.Actions.SaveButton, h.Actions.TranslateButton,
                    h.Actions.AskButton }
                .Select(AutomationProperties.GetName));
        h.RightClick(h.ToolbarCenter());
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.Empty(h.Commands);
    });

    [Fact]
    public void Right_click_outside_an_open_selection_only_warns_and_keeps_it() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        Click(h.Actions.AskButton);

        h.RightClick(EmptyPoint);

        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.True(h.Actions.Toolbar.IsPromptOpen);
        Assert.False(h.Window.VisualState.Actions.Tray.IsHitTestVisible);
        Assert.Equal(TestUiStrings.English.SelectionTooSmall, h.ToastText());
        Assert.Equal([typeof(AskDraftStarted)], h.Commands.Select(command => command.GetType()));
    });

    [Fact]
    public void Right_click_outside_a_text_selection_only_warns_and_keeps_it() => Run(() =>
    {
        var word = new GdiRectangle(300, 200, 60, 20);
        using var h = new Harness(ocr: new ImmediateRecognizer(new OcrDocument("en", new GdiSize(640, 400),
            [new OcrLine(0, 0, word, [new OcrWord(0, 0, 0, "hello", word)])])));
        WpfUi.PumpUntil(() => h.HoverShowsText(new Point(310, 210)), "OCR delivery timed out.");
        h.LeftDrag(new Point(310, 210), new Point(350, 210));
        Assert.True(h.Window.VisualState.TextSelection.Toolbar.IsOpen);

        h.RightClick(EmptyPoint);

        Assert.True(h.Window.VisualState.TextSelection.Toolbar.IsOpen);
        Assert.Equal(TestUiStrings.English.SelectionTooSmall, h.ToastText());
    });

    [Fact]
    public void Right_click_keeps_a_shown_translation() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        h.Translate();
        var mode = h.Window.Mode;
        var translated = h.Window.VisualState.Selection.Screenshot.Source;

        h.RightClick(EmptyPoint);

        Assert.Equal(mode, h.Window.Mode);
        Assert.Same(translated, h.Window.VisualState.Selection.Screenshot.Source);
        Assert.True(h.Actions.Toolbar.IsOpen);
    });

    [Fact]
    public void Escape_during_a_pending_right_press_releases_the_pointer() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        h.RightDown(EmptyPoint);
        Assert.True(h.Window.VisualState.Selection.InputSurface.IsMouseCaptured);

        h.Escape();

        Assert.False(h.Actions.Toolbar.IsOpen);
        Assert.False(h.Window.VisualState.Selection.InputSurface.IsMouseCaptured);
        h.LeftDrag(new Point(300, 200), new Point(420, 300));
        Assert.Single(h.Commands.OfType<VisualSelection>());
    });

    [Fact]
    public void Hidden_actions_are_left_out_of_the_toolbar_and_search_stays() => Run(() =>
    {
        using var h = new Harness(hiddenActions: SelectionToolbarAction.Ask | SelectionToolbarAction.Save);
        h.Select();
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.Equal(new[] { Visibility.Visible, Visibility.Visible, Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed },
            new[] { h.Actions.SearchButton, h.Actions.CopyButton, h.Actions.SaveButton, h.Actions.TranslateButton,
                    h.Actions.AskButton }
                .Select(button => button.Visibility));
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
        Assert.False(h.Actions.Toolbar.IsOpen);
        h.Escape();
        Assert.Single(h.Commands.OfType<CancelSession>());
    });

    [Fact]
    public void Escape_dismisses_image_selection_and_restores_action_tray() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        Assert.False(h.Window.VisualState.Actions.Tray.IsHitTestVisible);

        h.Escape();

        Assert.False(h.Actions.Toolbar.IsOpen);
        Assert.True(h.Window.VisualState.Selection.SelectionFrame.Data.IsEmpty());
        Assert.True(h.Window.VisualState.Actions.Tray.IsHitTestVisible);
        Assert.Equal(OverlayInteractionMode.Selecting, h.Window.Mode);
        Assert.Empty(h.Commands);
        h.Escape();
        Assert.Single(h.Commands.OfType<CancelSession>());
    });

    [Fact]
    public void Ask_without_typing_never_attaches_the_image() => Run(() =>
    {
        using var h = new Harness();
        h.Select();
        Click(h.Actions.AskButton);
        h.Select(new Point(20, 200), new Point(45, 225));
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
        h.Select(new Point(20, 200), new Point(45, 225));
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
        // The feedback delay is a DispatcherTimer, so it follows real time.
        DispatcherPump.For(300);
        Assert.DoesNotContain(h.Commands, command => command is CancelSession);
        Assert.True(DispatcherPump.Until(() => h.Commands.OfType<CancelSession>().Any()));
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
        Assert.Equal(provider == SearchProviderIds.TraceMoe ? OverlayInteractionMode.WidgetLoading : OverlayInteractionMode.Closing,
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
        Assert.Equal(TestUiStrings.English.ShowOriginal, AutomationProperties.GetName(h.Actions.TranslateButton));
        Assert.True(h.Actions.Toolbar.IsOpen);
        Assert.False(h.Window.VisualState.Selection.SelectionFrame.Data.IsEmpty());
        h.Select(new Point(20, 200), new Point(45, 225));
        Assert.Equal(OverlayInteractionMode.Selecting, h.Window.Mode);
        Assert.Equal(TestUiStrings.English.Translate, AutomationProperties.GetName(h.Actions.TranslateButton));
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
        Assert.Equal(OverlayInteractionMode.WidgetLoading, h.Window.Mode);

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
        h.Window.ShowWidgetResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(null)));
        var close = Descendants(h.Window.VisualState.Bottom.Stack).OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close);
        // The result becomes interactive after thread-pool continuations, not after a fixed delay.
        Assert.True(DispatcherPump.Until(() => Ancestors(close, h.Window.VisualState.Bottom.Stack).All(x => x.IsHitTestVisible)));
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

    private static IEnumerable<UIElement> Ancestors(DependencyObject element, UIElement root)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null && current != root; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement ancestor) yield return ancestor;
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

    private sealed class ImmediateRecognizer(OcrDocument document) : IOcrRecognizer
    {
        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source, string? requestedLanguageTag, CancellationToken cancellationToken) =>
            Task.FromResult(OcrRecognitionOutcome.Success(document));
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
            double scale = 1, bool overscan = false, bool consent = true,
            SelectionToolbarAction hiddenActions = SelectionToolbarAction.None, IOcrRecognizer? ocr = null)
        {
            Consent = consent;
            using (var graphics = System.Drawing.Graphics.FromImage(_frame)) graphics.Clear(System.Drawing.Color.Black);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            Window = new OverlayWindow(_frame, monitor, monitor, scale,
                new OverlayLaunchOptions(new OverlayOptions(0, 12), TestUiStrings.English,
                    [new(provider, provider)], provider, new SearchSessionOptions(HiddenToolbarActions: hiddenActions)),
                Commands.Add,
                TestOverlayControllers.CreateFactory(animationsEnabled: () => false, pointerPosition: _ => _point,
                    translationConsentAccepted: () => Consent, ocrRecognizer: ocr,
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

        internal void RightClick(Point? point = null)
        {
            RightDown(point ?? EmptyPoint);
            Raise(UIElement.MouseRightButtonUpEvent);
        }

        internal void RightDown(Point point)
        {
            _point = point;
            Raise(UIElement.MouseRightButtonDownEvent);
        }

        internal void LeftDrag(Point start, Point end)
        {
            var input = Window.VisualState.Selection.InputSurface;
            _point = start;
            Raise(UIElement.MouseLeftButtonDownEvent, MouseButton.Left);
            _point = end;
            input.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent, Source = input });
            Raise(UIElement.MouseLeftButtonUpEvent, MouseButton.Left);
        }

        // Hovering has no side effects, so it can be retried until the OCR document arrives.
        internal bool HoverShowsText(Point point)
        {
            var input = Window.VisualState.Selection.InputSurface;
            _point = point;
            input.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent, Source = input });
            return Window.Cursor == Cursors.IBeam;
        }

        internal Point ToolbarCenter()
        {
            var surface = Actions.Toolbar.Surface;
            return new Point(
                Canvas.GetLeft(surface) + surface.ActualWidth / 2,
                Canvas.GetTop(surface) + surface.ActualHeight / 2);
        }

        internal string ToastText()
        {
            var toastSlot = Assert.IsType<Grid>(Window.VisualState.Bottom.Stack.Children[0]);
            return Assert.IsType<TextBlock>(Assert.IsType<Border>(Assert.Single(toastSlot.Children)).Child).Text;
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

        private void Raise(RoutedEvent routedEvent, MouseButton button = MouseButton.Right)
        {
            var input = Window.VisualState.Selection.InputSurface;
            input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button)
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
