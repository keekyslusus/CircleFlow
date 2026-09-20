using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrOverlayControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Original_and_translated_results_are_reused_with_image_and_language_keys(bool noText)
    {
        Assert.Null(RunOnSta(() =>
        {
            var original = CreateFrozenSource();
            var translated = CreateFrozenSource();
            var recognizer = new CountingRecognizer { Outcome = noText ? OcrRecognitionOutcome.NoText() : OcrRecognitionOutcome.Success(Document()) };
            var delivered = new List<OcrRecognitionOutcome>();
            using var controller = new OcrOverlayController(original, Dispatcher.CurrentDispatcher, recognizer, "en-US", delivered.Add);
            void Run(Action action)
            {
                var count = delivered.Count;
                action();
                PumpUntil(() => delivered.Count > count);
            }
            Run(controller.Start);
            Run(() => controller.Restart(translated, "ru-RU"));
            for (var index = 0; index < 3; index++)
            {
                Run(() => controller.Restart(original, "EN-us"));
                Run(() => controller.Restart(translated, "ru-RU"));
            }
            Assert.Equal(2, recognizer.Calls);
            Assert.All(delivered, result => Assert.Same(recognizer.Outcome, result));
            Run(() => controller.Restart(translated, "de-DE"));
            Assert.Equal(3, recognizer.Calls);
            var replacement = CreateFrozenSource();
            Run(() => controller.Restart(replacement, "de-DE"));
            Assert.Equal(4, recognizer.Calls);
            Run(() => controller.Restart(translated, "de-DE"));
            Assert.Equal(5, recognizer.Calls);
            Run(() => controller.Restart(original, "en-US"));
            Assert.Equal(5, recognizer.Calls);
            controller.Dispose();
            controller.Restart(original, "en-US");
            Assert.Equal(5, recognizer.Calls);
            using var nextOverlay = new OcrOverlayController(original, Dispatcher.CurrentDispatcher, recognizer, "en-US", delivered.Add);
            Run(nextOverlay.Start);
            Assert.Equal(6, recognizer.Calls);
        }));
    }

    [Theory]
    [InlineData(OcrRecognitionStatus.Failed)]
    [InlineData(OcrRecognitionStatus.Canceled)]
    [InlineData(OcrRecognitionStatus.LanguageUnavailable)]
    public void Unsuccessful_recognition_is_retried_instead_of_cached(OcrRecognitionStatus status)
    {
        Assert.Null(RunOnSta(() =>
        {
            var original = CreateFrozenSource();
            var recognizer = new CountingRecognizer { Outcome = status switch
            {
                OcrRecognitionStatus.Canceled => OcrRecognitionOutcome.Canceled(),
                OcrRecognitionStatus.LanguageUnavailable => OcrRecognitionOutcome.LanguageUnavailable(),
                _ => OcrRecognitionOutcome.Failed()
            } };
            var results = new List<OcrRecognitionOutcome>();
            using var controller = new OcrOverlayController(original, Dispatcher.CurrentDispatcher, recognizer, null, results.Add);
            controller.Start();
            PumpUntil(() => results.Count == 1);
            recognizer.Outcome = OcrRecognitionOutcome.Success(Document());
            controller.Restart(original, null);
            PumpUntil(() => results.Count == 2);
            Assert.Equal(2, recognizer.Calls);
            Assert.Equal(OcrRecognitionStatus.Success, results[1].Status);
        }));
    }

    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(condition(), "OCR delivery timed out.");
    }

    private sealed class CountingRecognizer : IOcrRecognizer
    {
        internal int Calls { get; private set; }
        internal OcrRecognitionOutcome Outcome { get; set; } = OcrRecognitionOutcome.NoText();
        public Task<OcrRecognitionOutcome> RecognizeAsync(BitmapSource source, string? requestedLanguageTag, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Outcome);
        }
    }

    [Fact]
    public void Invalidate_suppresses_late_native_result_before_another_ocr_starts()
    {
        Assert.Null(RunOnSta(() =>
        {
            var source = CreateFrozenSource();
            var recognizer = new DeferredIgnoringCancellationRecognizer();
            var delivered = new List<OcrRecognitionOutcome>();
            using var controller = new OcrOverlayController(
                source, Dispatcher.CurrentDispatcher, recognizer, "en-US", delivered.Add);
            controller.Start();
            controller.Invalidate();
            recognizer.Completions[0].SetResult(OcrRecognitionOutcome.Success(Document()));
            PumpFor(TimeSpan.FromMilliseconds(30));
            Assert.Empty(delivered);
            controller.Restart(source, "ru-RU");
            recognizer.Completions[1].SetResult(OcrRecognitionOutcome.NoText());
            PumpUntil(() => delivered.Count == 1);
            Assert.Equal(OcrRecognitionStatus.NoText, delivered[0].Status);
        }));
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class DeferredIgnoringCancellationRecognizer : IOcrRecognizer
    {
        internal List<TaskCompletionSource<OcrRecognitionOutcome>> Completions { get; } = [];

        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source, string? requestedLanguageTag, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<OcrRecognitionOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Completions.Add(completion);
            return completion.Task;
        }
    }

    [Fact]
    public void Frozen_bitmap_result_is_delivered_through_overlay_dispatcher_and_status_is_logged()
    {
        var failure = RunOnSta(() =>
        {
            var source = CreateFrozenSource();
            Assert.Null(source.Dispatcher);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var document = Document();
            var recognizer = new AsyncRecognizer(OcrRecognitionOutcome.Success(document));
            var logDirectory = TestOutputPaths.NewTempDirectory(nameof(Frozen_bitmap_result_is_delivered_through_overlay_dispatcher_and_status_is_logged));
            var frame = new DispatcherFrame();
            var timedOut = false;
            OcrRecognitionOutcome? delivered = null;
            var deliveredOnUiThread = false;
            var timeout = StartTimeout(frame, () => timedOut = true);
            using var controller = new OcrOverlayController(
                source,
                dispatcher,
                recognizer,
                "en-US",
                outcome =>
                {
                    delivered = outcome;
                    deliveredOnUiThread = dispatcher.CheckAccess();
                    frame.Continue = false;
                },
                new PluginLog(logDirectory));

            controller.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();

            Assert.False(timedOut, "OCR result was not delivered before the timeout.");
            Assert.True(recognizer.ReceivedFrozenSource);
            Assert.True(deliveredOnUiThread);
            Assert.Same(document, Assert.IsType<OcrRecognitionOutcome>(delivered).Document);
            Assert.Contains(
                "OCR completed with status Success.",
                File.ReadAllText(Path.Combine(logDirectory, "plugin.log")));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Unexpected_recognizer_exception_is_delivered_as_failed_and_logged()
    {
        var failure = RunOnSta(() =>
        {
            var source = CreateFrozenSource();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var logDirectory = TestOutputPaths.NewTempDirectory(nameof(Unexpected_recognizer_exception_is_delivered_as_failed_and_logged));
            var frame = new DispatcherFrame();
            var timedOut = false;
            OcrRecognitionOutcome? delivered = null;
            var timeout = StartTimeout(frame, () => timedOut = true);
            using var controller = new OcrOverlayController(
                source,
                dispatcher,
                new FailingRecognizer(),
                null,
                outcome =>
                {
                    delivered = outcome;
                    frame.Continue = false;
                },
                new PluginLog(logDirectory));

            controller.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();

            Assert.False(timedOut, "Failed OCR outcome was not delivered before the timeout.");
            Assert.Equal(OcrRecognitionStatus.Failed, Assert.IsType<OcrRecognitionOutcome>(delivered).Status);
            var log = File.ReadAllText(Path.Combine(logDirectory, "plugin.log"));
            Assert.Contains("OCR recognition failed unexpectedly.", log);
            Assert.Contains(nameof(InvalidOperationException), log);
            Assert.Contains("OCR completed with status Failed.", log);
        });

        Assert.Null(failure);
    }

    private static BitmapSource CreateFrozenSource()
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        source.Freeze();
        return source;
    }

    private static OcrDocument Document()
    {
        var bounds = new Rectangle(0, 0, 1, 1);
        var word = new OcrWord(0, 0, 0, "text", bounds);
        return new OcrDocument("en-US", new Size(1, 1), [new OcrLine(0, 0, bounds, [word])]);
    }

    private static DispatcherTimer StartTimeout(DispatcherFrame frame, Action timedOut)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timedOut();
            frame.Continue = false;
        };
        timer.Start();
        return timer;
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }

    private sealed class AsyncRecognizer(OcrRecognitionOutcome outcome) : IOcrRecognizer
    {
        internal bool ReceivedFrozenSource { get; private set; }

        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source,
            string? requestedLanguageTag,
            CancellationToken cancellationToken) => Task.Run(() =>
            {
                ReceivedFrozenSource = source.IsFrozen;
                return outcome;
            }, cancellationToken);
    }

    private sealed class FailingRecognizer : IOcrRecognizer
    {
        public async Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source,
            string? requestedLanguageTag,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("Synthetic OCR failure.");
        }
    }
}
