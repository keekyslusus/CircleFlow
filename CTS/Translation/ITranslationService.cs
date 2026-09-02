namespace CircleToSearch.Translation;

using CircleToSearch.Ocr;
using GdiBitmap = System.Drawing.Bitmap;

public interface ITranslationService
{
    Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken cancellationToken);
    Task<IReadOnlyList<TranslationBlock>> TranslateScreenAsync(
        IReadOnlyList<OcrLineSnapshot> lines,
        GdiBitmap frame,
        double scale,
        string targetLanguage,
        CancellationToken cancellationToken);
}
