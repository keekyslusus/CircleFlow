using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleImageTranslationTests
{
    [Fact]
    public void Old_text_only_consent_does_not_authorize_screenshot_upload()
    {
        var settings = JsonSerializer.Deserialize<CircleToSearch.Settings.AppSettings>(
            "{\"TranslationPrivacyConsentAccepted\":true}")!;
        Assert.False(settings.ImageTranslationPrivacyConsentAccepted);
    }

    [Fact]
    public void Ocr_restart_cancels_original_and_drops_its_late_result()
    {
        OnSta(() =>
        {
            var recognizer = new DeferredRecognizer();
            var delivered = new List<OcrRecognitionOutcome>();
            var frame = new DispatcherFrame();
            using var controller = new OcrOverlayController(Source(), Dispatcher.CurrentDispatcher, recognizer, "en-US",
                outcome => { delivered.Add(outcome); frame.Continue = false; });
            controller.Start();
            controller.Restart(Source(), "ru-RU");
            Assert.True(recognizer.Cancellations[0].IsCancellationRequested);
            Assert.Equal(new[] { "en-US", "ru-RU" }, recognizer.Languages);
            recognizer.Completions[0].SetResult(OcrRecognitionOutcome.Failed());
            recognizer.Completions[1].SetResult(OcrRecognitionOutcome.NoText());
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
            Assert.Equal(OcrRecognitionStatus.NoText, Assert.Single(delivered).Status);
        });
    }

    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("zh-CN", "zh-CN")]
    public void Target_language_is_normalized_for_google(string input, string expected) =>
        Assert.Equal(expected, ScreenTranslationWorkflow.NormalizeTarget(input));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mixed_and_single_language_payloads_accept_optional_detected_language(bool includeLanguage)
    {
        var payload = new List<object> { new[] { Convert.ToBase64String(Png()), "image/png" }, "Hello Привет", "Hallo Hallo" };
        if (includeLanguage) payload.Add("en");
        var result = GoogleImageTranslationProtocol.ReadResponse(Response(payload));
        Assert.True(result.IsFrozen);
    }

    [Fact]
    public async Task Decoded_result_can_be_encoded_on_another_thread()
    {
        var result = GoogleImageTranslationProtocol.ReadResponse(Response(new object[]
        { new[] { Convert.ToBase64String(Png()), "image/png" }, "hello", "привет" }));
        await Task.Run(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            Assert.True(stream.Length > 0);
        });
    }

    [Fact]
    public void Image_only_response_is_not_reported_as_translation() =>
        Assert.Throws<InvalidDataException>(() => GoogleImageTranslationProtocol.ReadResponse(
            Response(new object[] { new[] { Convert.ToBase64String(Png()), "image/png" } })));

    [Theory]
    [InlineData("", "translated")]
    [InlineData("source", " ")]
    public void Blank_required_text_is_not_reported_as_translation(string source, string translated) =>
        Assert.Throws<InvalidDataException>(() => GoogleImageTranslationProtocol.ReadResponse(
            Response(new object[] { new[] { Convert.ToBase64String(Png()), "image/png" }, source, translated })));

    [Fact]
    public async Task Canceled_provider_cannot_publish_a_late_success()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new DelayedProvider();
        var workflow = new ScreenTranslationWorkflow(provider, NewLog());
        var task = workflow.TranslateAsync(Guid.NewGuid(), Source(), "ru-RU", cancellation.Token);
        cancellation.Cancel();
        provider.Completion.SetResult(Source());
        var result = await task;
        Assert.Equal(TranslationFailure.Canceled, result.Failure);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task Empty_request_id_is_rejected_before_provider_call()
    {
        var provider = new DelayedProvider();
        var workflow = new ScreenTranslationWorkflow(provider, NewLog());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.TranslateAsync(Guid.Empty, Source(), "ru-RU", CancellationToken.None));

        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public void Actual_overlay_factory_switches_prompt_and_ocr_to_displayed_image()
    {
        OnSta(() =>
        {
            using var bitmap = new System.Drawing.Bitmap(320, 200);
            var commands = new List<IOverlayCommand>();
            var recognizer = new DeferredRecognizer();
            var pointer = new Point(20, 20);
            var factory = TestOverlayControllers.CreateFactory(_ => { }, () => false,
                pointerPosition: _ => pointer, ocrRecognizer: recognizer);
            var service = TestSettings.Create(new CircleToSearch.Settings.AppSettings
            {
                PaddingPx = 0, LassoMinDiagonalPx = 10, OcrLanguageTag = "en-US", TranslationTargetLanguageTag = "ru-RU",
            });
            var session = CircleToSearch.Search.SearchSessionOptions.From(service.Snapshot,
                new OcrLanguageCatalog([new("en-US", "English")]), System.Globalization.CultureInfo.InvariantCulture,
                new CircleToSearch.Interop.KeyboardLanguageSnapshot(0, "en-US"));
            var launch = new OverlayLaunchOptions(new OverlayOptions(0, 10), TestUiStrings.English,
                [new(CircleToSearch.Search.SearchProviderIds.GoogleLens, "Google Lens")], CircleToSearch.Search.SearchProviderIds.GoogleLens, session);
            var bounds = new System.Drawing.Rectangle(0, 0, 320, 200);
            var window = new OverlayWindow(bitmap, bounds, bounds, 1, launch, commands.Add,
                factory, allowsTransparency: false, overscan: false, clickThroughOnCancel: false);
            Assert.Empty(recognizer.Languages);
            Assert.True(service.Apply(new CircleToSearch.Settings.SettingsEdits
                { OcrLanguageTag = "ja-JP", TranslationTargetLanguageTag = "ja-JP" }).Success);
            try
            {
                var visual = window.VisualState;
                var original = visual.Selection.Screenshot.Source;
                visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
                Assert.Equal("ru-RU", request.TargetLanguageTag);
                window.ShowTranslation(new(request.RequestId, Source()));
                Assert.Equal(Visibility.Visible, visual.Selection.Dim.Visibility);
                Assert.True(visual.Root.Children.IndexOf(visual.Selection.Screenshot) < visual.Root.Children.IndexOf(visual.Selection.Dim));
                Assert.Equal(TestUiStrings.English.TranslatedTextPrompt, visual.Actions.Prompt!.Text);
                Assert.Equal("ru-RU", Assert.Single(recognizer.Languages));
                Assert.Equal(320, ((BitmapSource)visual.Selection.Screenshot.Source).PixelWidth);
                visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(original, visual.Selection.Screenshot.Source);
                Assert.Equal(Visibility.Visible, visual.Selection.Dim.Visibility);
                Assert.Equal(TestUiStrings.English.SelectionPrompt, visual.Actions.Prompt.Text);
                Assert.Equal("en-US", recognizer.Languages[1]);

                visual.Selection.InputSurface.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = visual.Selection.InputSurface,
                });
                pointer = new Point(200, 140);
                visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                visual.Selection.InputSurface.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                    Source = visual.Selection.InputSurface,
                });
                Assert.Single(commands);
                Assert.Equal("ru-RU", recognizer.Languages[2]);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Image_request_does_not_wait_for_ocr_and_restore_uses_original()
    {
        OnSta(() =>
        {
            var source = Source();
            var visual = OverlayVisualFactory.CreateRoot(source, new Size(80, 40), 0, false, TestUiStrings.English);
            var commands = new List<IOverlayCommand>();
            var changed = new List<(BitmapSource Image, string? Language)>();
            var target = "ru";
            var state = new OverlayInteractionState();
            using var activity = new OverlayActivityPresenter(visual.ActivityHost, () => false);
            using var controller = new ScreenTranslationOverlayController(visual.TranslationAction, activity, visual.TranslationOverlay,
                visual.Bottom, visual.Effects, visual.Root, TestUiStrings.English, () => true, () => { }, () => target, commands.Add, mode => state.TransitionTo(mode), _ => { }, () => false,
                false, visual.Selection.Screenshot, (image, language) => changed.Add((image, language)));
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            Assert.Same(source, request.Image);
            Assert.True(controller.IsTranslating);
            controller.ShowResult(new(Guid.NewGuid(), Source()));
            Assert.Same(source, visual.Selection.Screenshot.Source);
            controller.ShowResult(new(request.RequestId, Source()));
            Assert.True(controller.IsImageShown);
            Assert.Equal("ru", Assert.Single(changed).Language);
            Assert.Equal(source.PixelWidth, changed[0].Image.PixelWidth);
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(source, visual.Selection.Screenshot.Source);
            Assert.Null(changed[1].Language);
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(commands);
            Assert.Same(changed[0].Image, visual.Selection.Screenshot.Source);
            Assert.Equal(OverlayInteractionMode.TranslationShown, state.Mode);
            Assert.False(controller.IsTranslating);
            Assert.Equal(Visibility.Collapsed, visual.TranslationAction.LoadingIndicator.Visibility);
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            target = "de";
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(source, Assert.IsType<ScreenTranslationRequested>(commands[1]).Image);
            var german = Assert.IsType<ScreenTranslationRequested>(commands[1]);
            Assert.Equal("de", german.TargetLanguageTag);
            controller.ShowFailure(german.RequestId, TranslationFailure.Network);
            var failureCard = Assert.IsType<Border>(Assert.Single(visual.TranslationOverlay.StateHost.Children));
            var retryButton = Assert.IsType<Button>(Assert.IsType<Grid>(failureCard.Child).Children[2]);
            retryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var retry = Assert.IsType<ScreenTranslationRequested>(commands[2]);
            controller.ShowResult(new(retry.RequestId, Source()));
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, commands.Count);
            Assert.Equal("de", changed[^1].Language);
            controller.CancelForClosing();
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, commands.Count);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });
    }

    [Fact]
    public void Canceled_overlay_ignores_late_image()
    {
        OnSta(() =>
        {
            var source = Source();
            var visual = OverlayVisualFactory.CreateRoot(source, new Size(80, 40), 0, false, TestUiStrings.English);
            var commands = new List<IOverlayCommand>();
            using var activity = new OverlayActivityPresenter(visual.ActivityHost, () => false);
            using var controller = new ScreenTranslationOverlayController(visual.TranslationAction, activity, visual.TranslationOverlay,
                visual.Bottom, visual.Effects, visual.Root, TestUiStrings.English, () => true, () => { }, () => "ru", commands.Add, _ => { }, _ => { }, () => false,
                false, visual.Selection.Screenshot);
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = Assert.IsType<ScreenTranslationRequested>(commands[0]);
            controller.CancelForClosing();
            controller.ShowResult(new(request.RequestId, Source()));
            Assert.False(controller.IsImageShown);
            Assert.Same(source, visual.Selection.Screenshot.Source);
            Assert.IsType<CancelScreenTranslation>(commands[1]);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });
    }

    private static string Response(object payload) => ")]}'\n123\n" + JsonSerializer.Serialize(new object[]
    { new object?[] { "wrb.fr", "WqWDPb", JsonSerializer.Serialize(payload) } });

    private static BitmapSource Source()
    {
        var bitmap = BitmapSource.Create(8, 4, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 4 * 4], 8 * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] Png()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Source()));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "translation-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class DelayedProvider : IImageTranslationProvider
    {
        public TaskCompletionSource<BitmapSource> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public Task<BitmapSource> TranslateAsync(BitmapSource source, string target, CancellationToken cancellation)
        {
            Calls++;
            return Completion.Task;
        }
    }

    private sealed class DeferredRecognizer : IOcrRecognizer
    {
        public List<TaskCompletionSource<OcrRecognitionOutcome>> Completions { get; } = [];
        public List<CancellationToken> Cancellations { get; } = [];
        public List<string?> Languages { get; } = [];
        public Task<OcrRecognitionOutcome> RecognizeAsync(BitmapSource source, string? requestedLanguageTag, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<OcrRecognitionOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            Completions.Add(completion);
            Cancellations.Add(cancellationToken);
            Languages.Add(requestedLanguageTag);
            return completion.Task;
        }
    }
}
