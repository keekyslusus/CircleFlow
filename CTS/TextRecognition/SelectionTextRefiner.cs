using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Ocr;

namespace CircleToSearch.TextRecognition;

public sealed class SelectionTextRefiner(
    ILanguageOcrRecognizer languageRecognizer,
    OcrLanguageCatalog languageCatalog,
    AutomaticOcrLanguageResolver languageResolver,
    OcrDocumentMerger merger,
    OcrUnicodeScriptClassifier scriptClassifier,
    int maximumDimension = 0) : ISelectionTextRefiner
{
    public async Task<SelectionTextRefinement> RefineAsync(
        BitmapSource frozenFrame,
        TextSelectionRange selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frozenFrame);
        ArgumentNullException.ThrowIfNull(selection);
        var languages = languageResolver.Resolve(languageCatalog.AvailableLanguages);
        if (languages.Count == 0) return SelectionTextRefinement.NoText();
        try
        {
            var resolvedLines = new List<string>(selection.Lines.Count);
            var recognizedAny = false;
            foreach (var line in selection.Lines.OrderBy(line => line.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cropBounds = CreateCropBounds(line.BoundsPx, frozenFrame.PixelWidth, frozenFrame.PixelHeight);
                var scale = CalculateScale(
                    line.BoundsPx.Height,
                    cropBounds,
                    maximumDimension > 0 ? maximumDimension : checked((int)OcrEngine.MaxImageDimension));
                var crop = CreateCrop(frozenFrame, cropBounds, scale);
                var outcome = await RecognizeAsync(
                    crop,
                    languages,
                    line.PreliminaryText,
                    cancellationToken).ConfigureAwait(false);
                if (outcome.Status == OcrRecognitionStatus.Canceled) return SelectionTextRefinement.Canceled();
                var refined = outcome.Status == OcrRecognitionStatus.Success
                    ? ExtractSelectedText(outcome.Document!, line.BoundsPx, cropBounds, scale)
                    : null;
                if (!string.IsNullOrWhiteSpace(refined))
                {
                    recognizedAny = true;
                    resolvedLines.Add(refined);
                }
                else
                {
                    resolvedLines.Add(line.PreliminaryText);
                }
            }
            var text = string.Join(Environment.NewLine, resolvedLines);
            return recognizedAny && !string.IsNullOrWhiteSpace(text)
                ? SelectionTextRefinement.Success(text)
                : SelectionTextRefinement.NoText();
        }
        catch (OperationCanceledException)
        {
            return SelectionTextRefinement.Canceled();
        }
        catch
        {
            return SelectionTextRefinement.Failed();
        }
    }

    internal static Rectangle CreateCropBounds(Rectangle selected, int frameWidth, int frameHeight)
    {
        var padding = Math.Max(2, (int)Math.Round(selected.Height * 0.25));
        return Rectangle.FromLTRB(
            Math.Max(0, selected.Left - padding),
            Math.Max(0, selected.Top - padding),
            Math.Min(frameWidth, selected.Right + padding),
            Math.Min(frameHeight, selected.Bottom + padding));
    }

    internal static double CalculateScale(int sourceLineHeight, Rectangle cropBounds, int maxDimension)
    {
        var desired = Math.Clamp(40d / Math.Max(1, sourceLineHeight), 1, 3);
        var dimensionLimit = Math.Min((double)maxDimension / cropBounds.Width, (double)maxDimension / cropBounds.Height);
        return Math.Max(1, Math.Min(desired, dimensionLimit));
    }

    private async Task<OcrRecognitionOutcome> RecognizeAsync(
        BitmapSource crop,
        IReadOnlyList<string> languages,
        string preliminaryText,
        CancellationToken cancellationToken)
    {
        var outcomes = await Task.WhenAll(languages.Select(language =>
            languageRecognizer.RecognizeAsync(crop, language, cancellationToken))).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (outcomes.Any(outcome => outcome.Status == OcrRecognitionStatus.Canceled))
            return OcrRecognitionOutcome.Canceled();
        if (outcomes.Any(outcome => outcome.Status is OcrRecognitionStatus.Failed or
                OcrRecognitionStatus.PlatformUnavailable or OcrRecognitionStatus.LanguageUnavailable))
            return OcrRecognitionOutcome.Failed();
        var documents = outcomes.Where(outcome => outcome.Status == OcrRecognitionStatus.Success)
            .Select(outcome => outcome.Document!).ToArray();
        if (documents.Length == 0) return OcrRecognitionOutcome.NoText();
        if (outcomes.Any(outcome => outcome.Status == OcrRecognitionStatus.NoText) &&
            documents.Length == 1 &&
            !MatchesPreliminaryScripts(preliminaryText, DocumentText(documents[0])))
            return OcrRecognitionOutcome.NoText();
        var merged = merger.Merge(documents, languages);
        return merged.Lines.Count == 0 ? OcrRecognitionOutcome.NoText() : OcrRecognitionOutcome.Success(merged);
    }

    private bool MatchesPreliminaryScripts(string preliminaryText, string candidateText)
    {
        var preliminary = scriptClassifier.Analyze(preliminaryText);
        if (preliminary.LetterCount == 0) return true;
        var candidate = scriptClassifier.Analyze(candidateText);
        return preliminary.SignificantScripts.All(script => candidate.Count(script) > 0);
    }

    private static string DocumentText(OcrDocument document) =>
        string.Join(Environment.NewLine, document.Lines.OrderBy(line => line.Order).Select(line => line.Text));

    private static BitmapSource CreateCrop(BitmapSource source, Rectangle bounds, double scale)
    {
        var cropped = new CroppedBitmap(source, new Int32Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        BitmapSource result = cropped;
        if (scale > 1.001)
        {
            var transformed = new TransformedBitmap();
            transformed.BeginInit();
            transformed.Source = cropped;
            transformed.Transform = new ScaleTransform(scale, scale);
            RenderOptions.SetBitmapScalingMode(transformed, BitmapScalingMode.HighQuality);
            transformed.EndInit();
            result = transformed;
        }
        result.Freeze();
        return result;
    }

    private static string? ExtractSelectedText(
        OcrDocument document,
        Rectangle selectedBounds,
        Rectangle cropBounds,
        double requestedScale)
    {
        var scaleX = document.PixelSize.Width / (double)cropBounds.Width;
        var scaleY = document.PixelSize.Height / (double)cropBounds.Height;
        if (!double.IsFinite(scaleX) || scaleX <= 0) scaleX = requestedScale;
        if (!double.IsFinite(scaleY) || scaleY <= 0) scaleY = requestedScale;
        var allowance = selectedBounds.Height * 0.1;
        var left = (selectedBounds.Left - cropBounds.Left - allowance) * scaleX;
        var top = (selectedBounds.Top - cropBounds.Top - allowance) * scaleY;
        var right = (selectedBounds.Right - cropBounds.Left + allowance) * scaleX;
        var bottom = (selectedBounds.Bottom - cropBounds.Top + allowance) * scaleY;
        var words = document.Words.Where(word =>
        {
            var centerX = word.BoundsPx.Left + word.BoundsPx.Width / 2d;
            var centerY = word.BoundsPx.Top + word.BoundsPx.Height / 2d;
            return centerX >= left && centerX <= right && centerY >= top && centerY <= bottom;
        }).OrderBy(word => word.ReadingOrder).Select(word => word.Text).ToArray();
        return words.Length == 0 ? null : string.Join(' ', words);
    }
}
