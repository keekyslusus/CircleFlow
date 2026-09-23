using CircleToSearch.Capture;
using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Search;

internal sealed class ImageAskWorkflow(
    Func<byte[], string, IVisualSearchBrowserOperation> createOperation,
    Func<GdiBitmap, GdiRectangle, int, byte[]> crop,
    VisualSearchResultPresenter presenter,
    UiStrings strings,
    PluginLog log)
{
    public async Task ExecuteAsync(
        SelectionOutcome selection,
        string question,
        Action onUploadStarted,
        int maxLongSidePx,
        CancellationToken cancellationToken)
    {
        byte[] jpeg;
        try { jpeg = crop(selection.FrozenFrame, selection.Bounds, maxLongSidePx); }
        finally { selection.Dispose(); }

        onUploadStarted();
        log.Info(nameof(ImageAskWorkflow), "asking Google AI Mode about the selected image");
        // Only Google AI Mode accepts an image with a question, so Ask ignores the selected provider.
        var prepared = PreparedVisualSearch.ForBrowserOperation(
            createOperation(jpeg, question), externalFallbackUrl: null);
        await presenter.PresentAsync(
            new RoutedVisualSearchPreparation(
                SearchProviderIds.GoogleLens,
                strings.GoogleLensProviderName,
                VisualSearchPreparationOutcome.Ready(prepared),
                UsedFallback: false),
            cancellationToken).ConfigureAwait(false);
    }
}
