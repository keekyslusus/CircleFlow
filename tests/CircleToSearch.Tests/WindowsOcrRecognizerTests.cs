using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.TextRecognition;
using Xunit;
using GdiSize = System.Drawing.Size;
using System.Globalization;

namespace CircleToSearch.Tests;

public sealed class WindowsOcrRecognizerTests
{
    [Fact]
    public void Scaled_word_bounds_map_back_to_capture_pixels()
    {
        var mapped = WindowsOcrRecognizer.MapRectangle(
            new Windows.Foundation.Rect(10, 20, 30, 40),
            2,
            1.5,
            new GdiSize(200, 200));

        Assert.Equal(new Rectangle(20, 30, 60, 60), mapped);
    }

    [Fact]
    public async Task Pre_canceled_recognition_returns_canceled_outcome()
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await new WindowsOcrRecognizer().RecognizeAsync(source, null, cancellation.Token);

        Assert.Equal(OcrRecognitionStatus.Canceled, outcome.Status);
    }

    [Fact]
    public async Task Windows_ocr_runtime_recognizes_a_synthetic_frozen_frame()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, 500, 140));
            drawing.DrawText(new FormattedText(
                "HELLO 123",
                CultureInfo.GetCultureInfo("en-US"),
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                64,
                System.Windows.Media.Brushes.Black,
                1), new System.Windows.Point(20, 20));
        }
        var source = new RenderTargetBitmap(500, 140, 96, 96, PixelFormats.Pbgra32);
        source.Render(visual);
        source.Freeze();

        var outcome = await new WindowsOcrRecognizer().RecognizeAsync(source, "en-US", CancellationToken.None);

        Assert.Equal(OcrRecognitionStatus.Success, outcome.Status);
        Assert.Contains(outcome.Document!.Words, word => word.Text.Contains("HELLO", StringComparison.OrdinalIgnoreCase));
    }
}
