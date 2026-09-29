using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace CircleToSearch.TextRecognition;

public sealed class WindowsOcrRecognizer : IOcrRecognizer
{
    public async Task<OcrRecognitionOutcome> RecognizeAsync(
        BitmapSource source,
        string? requestedLanguageTag,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engine = CreateEngine(requestedLanguageTag);
            if (engine is null) return OcrRecognitionOutcome.LanguageUnavailable();

            var prepared = await Task.Run(() => PreparePixels(source, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                prepared.Pixels.AsBuffer(),
                BitmapPixelFormat.Bgra8,
                prepared.Width,
                prepared.Height,
                BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            var document = BuildDocument(
                engine.RecognizerLanguage.LanguageTag,
                new Size(source.PixelWidth, source.PixelHeight),
                prepared.Width,
                prepared.Height,
                result);
            return document.Lines.Count == 0
                ? OcrRecognitionOutcome.NoText()
                : OcrRecognitionOutcome.Success(document);
        }
        catch (OperationCanceledException)
        {
            return OcrRecognitionOutcome.Canceled();
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or TypeLoadException or DllNotFoundException)
        {
            return OcrRecognitionOutcome.PlatformUnavailable();
        }
        catch
        {
            return OcrRecognitionOutcome.Failed();
        }
    }

    private static OcrEngine? CreateEngine(string? requestedLanguageTag)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguageTag))
            return OcrEngine.TryCreateFromUserProfileLanguages();
        var language = OcrLanguageCatalog.TryCreateLanguage(requestedLanguageTag);
        return language is null || !OcrEngine.IsLanguageSupported(language)
            ? null
            : OcrEngine.TryCreateFromLanguage(language);
    }

    private static PreparedPixels PreparePixels(BitmapSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var sourceWidth = bgra.PixelWidth;
        var sourceHeight = bgra.PixelHeight;
        var ratio = Math.Min(1d, (double)OcrEngine.MaxImageDimension / Math.Max(sourceWidth, sourceHeight));
        var width = Math.Max(1, (int)Math.Floor(sourceWidth * ratio));
        var height = Math.Max(1, (int)Math.Floor(sourceHeight * ratio));
        var sourceStride = checked(sourceWidth * 4);
        var sourcePixels = new byte[checked(sourceStride * sourceHeight)];
        bgra.CopyPixels(sourcePixels, sourceStride, 0);
        if (width == sourceWidth && height == sourceHeight)
            return new PreparedPixels(sourcePixels, width, height);

        var resized = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = Math.Min(sourceHeight - 1, (int)((long)y * sourceHeight / height));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, (int)((long)x * sourceWidth / width));
                Buffer.BlockCopy(sourcePixels, (sourceY * sourceWidth + sourceX) * 4, resized, (y * width + x) * 4, 4);
            }
        }
        return new PreparedPixels(resized, width, height);
    }

    internal static OcrDocument BuildDocument(
        string languageTag,
        Size originalSize,
        int recognizedWidth,
        int recognizedHeight,
        OcrResult result)
    {
        var scaleX = (double)originalSize.Width / recognizedWidth;
        var scaleY = (double)originalSize.Height / recognizedHeight;
        var lineId = 0;
        var wordId = 0;
        var readingOrder = 0;
        var lines = new List<OcrLine>();
        foreach (var sourceLine in result.Lines)
        {
            var currentLineId = lineId++;
            var words = new List<OcrWord>();
            foreach (var sourceWord in sourceLine.Words)
            {
                if (string.IsNullOrWhiteSpace(sourceWord.Text)) continue;
                var bounds = MapRectangle(sourceWord.BoundingRect, scaleX, scaleY, originalSize);
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                words.Add(new OcrWord(wordId++, currentLineId, readingOrder++, sourceWord.Text, bounds));
            }
            if (words.Count == 0) continue;
            lines.Add(new OcrLine(
                currentLineId,
                lines.Count,
                words.Select(word => word.BoundsPx).Aggregate(Rectangle.Union),
                words));
        }
        return new OcrDocument(languageTag, originalSize, lines);
    }

    internal static Rectangle MapRectangle(
        Windows.Foundation.Rect rectangle,
        double scaleX,
        double scaleY,
        Size bounds)
    {
        var left = Math.Clamp((int)Math.Floor(rectangle.Left * scaleX), 0, bounds.Width);
        var top = Math.Clamp((int)Math.Floor(rectangle.Top * scaleY), 0, bounds.Height);
        var right = Math.Clamp((int)Math.Ceiling(rectangle.Right * scaleX), left, bounds.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(rectangle.Bottom * scaleY), top, bounds.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private sealed record PreparedPixels(byte[] Pixels, int Width, int Height);
}
