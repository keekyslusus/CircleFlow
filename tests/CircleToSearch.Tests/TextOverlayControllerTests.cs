namespace CircleToSearch.Tests;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Ocr;
using CircleToSearch.Translation;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

public sealed class TextOverlayControllerTests
{
    private sealed class DummyTranslationService : ITranslationService
    {
        public Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken cancellationToken) =>
            Task.FromResult("Переведено: " + text);

        public Task<IReadOnlyList<TranslationBlock>> TranslateScreenAsync(
            IReadOnlyList<OcrLineSnapshot> lines,
            GdiBitmap frame,
            double scale,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranslationBlock>>([]);
    }

    [Fact]
    public void Selection_and_copy_workflow_updates_mode_and_clipboard()
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = new TextSelectionVisual();
            var toolbar = new FloatingTextToolbarVisual(false, TestUiStrings.English);
            var root = new Grid { Width = 800, Height = 600 };
            var window = new Window();
            var clipboard = new List<string>();
            var toasts = new List<ToastNotification>();
            var searches = new List<GdiRectangle>();
            var mode = OverlayInteractionMode.Selecting;

            using var controller = new TextOverlayController(
                visual,
                toolbar,
                root,
                window,
                TestUiStrings.English,
                clipboard.Add,
                toasts.Add,
                searches.Add,
                new DummyTranslationService(),
                () => mode,
                m => mode = m,
                1.0,
                false);

            var word1 = new OcrWordSnapshot("Hello", new Rect(10, 10, 40, 20), new GdiRectangle(10, 10, 40, 20));
            var word2 = new OcrWordSnapshot("World", new Rect(60, 10, 50, 20), new GdiRectangle(60, 10, 50, 20));
            var line = new OcrLineSnapshot("Hello World", new Rect(10, 10, 100, 20), new GdiRectangle(10, 10, 100, 20), [word1, word2]);
            var snapshot = new OcrScreenSnapshot([line], [word1, word2], "Hello World");

            controller.SetSnapshot(snapshot);

            Assert.True(controller.IsOverText(new Point(20, 20)));
            Assert.False(controller.IsOverText(new Point(200, 200)));

            var started = controller.StartSelection(new Point(20, 20));
            Assert.True(started);
            Assert.Equal(OverlayInteractionMode.TextSelection, mode);

            controller.UpdateDrag(new Point(80, 20));
            Assert.True(controller.HasSelection);
            Assert.Equal("Hello World", controller.SelectedText);

            controller.FinishDrag();
            Assert.Equal(Visibility.Visible, toolbar.Root.Visibility);

            var copied = controller.TryCopySelection();
            Assert.True(copied);
            Assert.Equal(["Hello World"], clipboard);
            Assert.Single(toasts);
            Assert.Equal(TestUiStrings.English.TextCopied, toasts[0].Message);
            Assert.Equal(OverlayInteractionMode.Selecting, mode);
            Assert.False(controller.HasSelection);
            Assert.Equal(Visibility.Collapsed, toolbar.Root.Visibility);
        }));
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}
