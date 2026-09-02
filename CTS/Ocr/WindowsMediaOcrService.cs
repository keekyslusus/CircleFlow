namespace CircleToSearch.Ocr;

using System.IO;
using System.Windows;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

public sealed class WindowsMediaOcrService : IOcrService
{
    private readonly OcrEngine? _engine;
    private readonly PluginLog? _log;

    public bool IsAvailable => _engine is not null;

    public WindowsMediaOcrService(PluginLog? log = null)
    {
        _log = log;
        _engine = InitializeEngine();
        _log?.Info(nameof(WindowsMediaOcrService), _engine is null
            ? "Windows Media OCR engine is not available"
            : $"Windows Media OCR initialized for language: {_engine.RecognizerLanguage.LanguageTag}");
    }

    public async Task<OcrScreenSnapshot> RecognizeAsync(
        GdiBitmap frame,
        double scale,
        CancellationToken cancellationToken)
    {
        if (_engine is null) return OcrScreenSnapshot.Empty;
        ArgumentNullException.ThrowIfNull(frame);
        if (scale <= 0) scale = 1.0;

        try
        {
            using var memoryStream = new MemoryStream();
            frame.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
            memoryStream.Position = 0;

            var randomAccessStream = new InMemoryRandomAccessStream();
            using (var outputStream = randomAccessStream.GetOutputStreamAt(0))
            using (var writer = new DataWriter(outputStream))
            {
                writer.WriteBytes(memoryStream.ToArray());
                await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
                await outputStream.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            }

            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream).AsTask(cancellationToken).ConfigureAwait(false);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);

            var result = await _engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken).ConfigureAwait(false);
            if (result is null || result.Lines is null || result.Lines.Count == 0)
                return OcrScreenSnapshot.Empty;

            var lines = new List<OcrLineSnapshot>(result.Lines.Count);
            var allWords = new List<OcrWordSnapshot>();

            foreach (var line in result.Lines)
            {
                var words = new List<OcrWordSnapshot>(line.Words.Count);
                foreach (var word in line.Words)
                {
                    var rect = word.BoundingRect;
                    var pixelRect = new GdiRectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
                    var dipRect = new Rect(rect.X / scale, rect.Y / scale, rect.Width / scale, rect.Height / scale);
                    var wordSnapshot = new OcrWordSnapshot(word.Text, dipRect, pixelRect);
                    words.Add(wordSnapshot);
                    allWords.Add(wordSnapshot);
                }

                var lineDipRect = words.Count > 0
                    ? words.Select(w => w.DipRect).Aggregate(Rect.Union)
                    : Rect.Empty;
                var linePixelRect = words.Count > 0
                    ? new GdiRectangle(
                        (int)Math.Round(lineDipRect.X * scale),
                        (int)Math.Round(lineDipRect.Y * scale),
                        (int)Math.Round(lineDipRect.Width * scale),
                        (int)Math.Round(lineDipRect.Height * scale))
                    : GdiRectangle.Empty;

                lines.Add(new OcrLineSnapshot(line.Text, lineDipRect, linePixelRect, words));
            }

            var fullText = string.Join(Environment.NewLine, lines.Select(l => l.Text));
            return new OcrScreenSnapshot(lines, allWords, fullText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Error(nameof(WindowsMediaOcrService), "OCR recognition failed", ex);
            return OcrScreenSnapshot.Empty;
        }
    }

    private static OcrEngine? InitializeEngine()
    {
        try
        {
            var userProfileEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (userProfileEngine is not null) return userProfileEngine;

            if (OcrEngine.AvailableRecognizerLanguages.Count > 0)
            {
                return OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
            }
        }
        catch
        {
        }
        return null;
    }
}
