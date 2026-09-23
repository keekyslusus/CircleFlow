using CircleToSearch.Capture;
using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Search;

internal sealed class ImageAskWorkflow(
    Func<Task<byte[]>, Task<string>, IVisualSearchBrowserOperation> createOperation,
    Func<GdiBitmap, GdiRectangle, int, byte[]> crop,
    VisualSearchResultPresenter presenter,
    UiStrings strings,
    PluginLog log)
{
    public byte[] Encode(SelectionOutcome selection, int maxLongSidePx)
    {
        try { return crop(selection.FrozenFrame, selection.Bounds, maxLongSidePx); }
        finally { selection.Dispose(); }
    }

    public Task PresentAsync(Task<byte[]> image, Task<string> question, CancellationToken cancellationToken)
    {
        log.Info(nameof(ImageAskWorkflow), "preparing Google AI Mode for a question about the selected image");
        // Only Google AI Mode accepts an image with a question, so Ask ignores the selected provider.
        var prepared = PreparedVisualSearch.ForBrowserOperation(
            createOperation(image, question), externalFallbackUrl: null, revealAfter: question);
        return presenter.PresentAsync(
            new RoutedVisualSearchPreparation(
                SearchProviderIds.GoogleLens,
                strings.GoogleLensProviderName,
                VisualSearchPreparationOutcome.Ready(prepared),
                UsedFallback: false),
            cancellationToken);
    }
}
