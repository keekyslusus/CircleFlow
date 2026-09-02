namespace CircleToSearch.Ocr;

using GdiBitmap = System.Drawing.Bitmap;

public interface IOcrService
{
    bool IsAvailable { get; }
    Task<OcrScreenSnapshot> RecognizeAsync(GdiBitmap frame, double scale, CancellationToken cancellationToken);
}
