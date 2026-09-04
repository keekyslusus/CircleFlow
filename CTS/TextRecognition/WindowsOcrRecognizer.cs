using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace CircleToSearch.TextRecognition;

using Int32Rect = System.Windows.Int32Rect;

public sealed class WindowsOcrRecognizer : ILanguageOcrRecognizer, IDisposable
{
    private readonly OcrFrameTiler _tiler;
    private readonly SemaphoreSlim _scheduler;
    private bool _disposed;

    public WindowsOcrRecognizer(OcrFrameTiler? tiler = null, int maximumConcurrency = 2)
    {
        if (maximumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _tiler = tiler ?? new OcrFrameTiler();
        _scheduler = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public async Task<OcrRecognitionOutcome> RecognizeAsync(
        BitmapSource source,
        string languageTag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engine = CreateEngine(languageTag);
            if (engine is null) return OcrRecognitionOutcome.LanguageUnavailable();
            var size = new Size(source.PixelWidth, source.PixelHeight);
            var tiles = _tiler.Create(size.Width, size.Height, checked((int)OcrEngine.MaxImageDimension));
            var collected = new List<RawLine>();
            foreach (var tile in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prepared = await Task.Run(() => PreparePixels(source, tile.BoundsPx, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                    prepared.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, prepared.Width, prepared.Height,
                    BitmapAlphaMode.Premultiplied);
                cancellationToken.ThrowIfCancellationRequested();
                await _scheduler.WaitAsync(cancellationToken).ConfigureAwait(false);
                OcrResult result;
                try
                {
                    result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _scheduler.Release();
                }
                cancellationToken.ThrowIfCancellationRequested();
                collected.AddRange(ReadTile(result, tile, size));
            }
            var document = BuildDocument(engine.RecognizerLanguage.LanguageTag, size, collected);
            return document.Lines.Count == 0 ? OcrRecognitionOutcome.NoText() : OcrRecognitionOutcome.Success(document);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scheduler.Dispose();
    }

    private static OcrEngine? CreateEngine(string languageTag)
    {
        var language = OcrLanguageCatalog.TryCreateLanguage(languageTag);
        return language is null || !OcrEngine.IsLanguageSupported(language)
            ? null
            : OcrEngine.TryCreateFromLanguage(language);
    }

    private static PreparedPixels PreparePixels(BitmapSource source, Rectangle bounds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(bounds.Width * 4);
        var pixels = new byte[checked(stride * bounds.Height)];
        bgra.CopyPixels(new Int32Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), pixels, stride, 0);
        return new PreparedPixels(pixels, bounds.Width, bounds.Height);
    }

    private static IEnumerable<RawLine> ReadTile(OcrResult result, OcrFrameTile tile, Size sourceSize)
    {
        foreach (var sourceLine in result.Lines)
        {
            var words = new List<RawWord>();
            foreach (var sourceWord in sourceLine.Words)
            {
                if (string.IsNullOrWhiteSpace(sourceWord.Text)) continue;
                var local = MapRectangle(sourceWord.BoundingRect, 1, 1, tile.BoundsPx.Size);
                var mapped = MapRectangle(local, tile.BoundsPx.Location, sourceSize);
                if (mapped.Width <= 0 || mapped.Height <= 0 || !tile.Owns(mapped)) continue;
                words.Add(new RawWord(sourceWord.Text, mapped));
            }
            if (words.Count > 0) yield return new RawLine(words);
        }
    }

    private static OcrDocument BuildDocument(string languageTag, Size size, IReadOnlyList<RawLine> rawLines)
    {
        var candidates = rawLines.OrderBy(line => line.Bounds.Top).ThenBy(line => line.Bounds.Left).ToArray();
        var groups = new List<List<RawLine>>();
        foreach (var candidate in candidates)
        {
            var group = groups.FirstOrDefault(existing => SamePhysicalLine(existing[0].Bounds, candidate.Bounds));
            if (group is null) groups.Add([candidate]);
            else group.Add(candidate);
        }
        var ordered = groups.Select(group => new RawLine(group.SelectMany(line => line.Words).ToArray()))
            .OrderBy(line => line.Bounds.Top).ThenBy(line => line.Bounds.Left).ToArray();
        var lines = new List<OcrLine>(ordered.Length);
        var wordId = 0;
        var readingOrder = 0;
        for (var lineId = 0; lineId < ordered.Length; lineId++)
        {
            var words = ordered[lineId].Words.OrderBy(word => word.Bounds.Left)
                .Select(word => new OcrWord(wordId++, lineId, readingOrder++, languageTag, word.Text, word.Bounds)).ToArray();
            lines.Add(new OcrLine(lineId, lineId, languageTag,
                words.Select(word => word.BoundsPx).Aggregate(Rectangle.Union), words));
        }
        return new OcrDocument(languageTag, size, lines);
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
        var raw = result.Lines.Select(sourceLine => new RawLine(sourceLine.Words
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new RawWord(word.Text, MapRectangle(word.BoundingRect, scaleX, scaleY, originalSize))).ToArray()))
            .Where(line => line.Words.Count > 0).ToArray();
        return BuildDocument(languageTag, originalSize, raw);
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

    internal static Rectangle MapRectangle(Rectangle rectangle, Point offset, Size bounds) => Rectangle.FromLTRB(
        Math.Clamp(rectangle.Left + offset.X, 0, bounds.Width),
        Math.Clamp(rectangle.Top + offset.Y, 0, bounds.Height),
        Math.Clamp(rectangle.Right + offset.X, 0, bounds.Width),
        Math.Clamp(rectangle.Bottom + offset.Y, 0, bounds.Height));

    private static bool SamePhysicalLine(Rectangle first, Rectangle second)
    {
        var verticalOverlap = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        if (verticalOverlap < Math.Min(first.Height, second.Height) * 0.6) return false;
        var horizontalOverlap = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var gap = horizontalOverlap > 0 ? 0 : Math.Max(first.Left, second.Left) - Math.Min(first.Right, second.Right);
        return horizontalOverlap > 0 || gap <= Math.Max(first.Height, second.Height);
    }

    private sealed record PreparedPixels(byte[] Pixels, int Width, int Height);
    private sealed record RawWord(string Text, Rectangle Bounds);
    private sealed record RawLine(IReadOnlyList<RawWord> Words)
    {
        internal Rectangle Bounds => Words.Select(word => word.Bounds).Aggregate(Rectangle.Union);
    }
}
