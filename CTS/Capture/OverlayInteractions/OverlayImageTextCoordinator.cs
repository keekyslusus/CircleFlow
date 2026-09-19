using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class OverlayImageTextCoordinator(
    PointerGestureRouter pointer,
    TextSelectionOverlayController textSelection,
    OcrOverlayController ocr,
    TextBlock? prompt,
    UiStrings strings,
    string? originalOcrLanguageTag)
{
    internal void OnImageChanged(BitmapSource image, string? language)
    {
        pointer.Cancel();
        textSelection.SetDocument(null);
        if (prompt is not null)
            prompt.Text = language is null ? strings.SelectionPrompt : strings.TranslatedTextPrompt;
        ocr.Restart(image, language ?? originalOcrLanguageTag);
    }
}
