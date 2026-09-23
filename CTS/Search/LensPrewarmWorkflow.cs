using CircleToSearch.Capture;
using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Search;

internal sealed class LensPrewarmWorkflow(
    Func<Task<byte[]>, IVisualSearchBrowserOperation> createOperation,
    Func<GdiBitmap, GdiRectangle, int, byte[]> crop,
    VisualSearchResultPresenter presenter,
    UiStrings strings,
    PluginLog log)
{
    public byte[] Encode(SelectionOutcome selection, int maxLongSidePx) => selection.Encode(crop, maxLongSidePx);

    public Task PresentAsync(Task<byte[]> image, Task revealAfter, CancellationToken cancellationToken)
    {
        log.Info(nameof(LensPrewarmWorkflow), "warming Google Lens while the selection is drawn");
        var prepared = PreparedVisualSearch.ForBrowserOperation(
            createOperation(image), externalFallbackUrl: null, revealAfter);
        return presenter.PresentAsync(
            new RoutedVisualSearchPreparation(
                SearchProviderIds.GoogleLens,
                strings.GoogleLensProviderName,
                VisualSearchPreparationOutcome.Ready(prepared),
                UsedFallback: false),
            cancellationToken);
    }
}
