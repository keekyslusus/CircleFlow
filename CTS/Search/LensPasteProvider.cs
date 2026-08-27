using System.Diagnostics;

namespace CircleToSearch.Search;

// Fallback flow for when Google's anonymous upload endpoint accepts the POST but never processes
// the image (verified 2026-08-28): put the PNG on the clipboard, open Lens, auto-paste there.
public sealed class LensPasteProvider : IVisualSearchProvider
{
    public const string LensHomeUrl = "https://lens.google.com";

    private readonly Func<byte[], Task<bool>> _copyToClipboard;
    private readonly Func<string, Process?> _openLens;
    private readonly Func<Task> _startPasteWatch;
    private readonly BrowserPasteInjector _pasteInjector;
    private readonly PluginLog _log;

    public LensPasteProvider(
        Func<byte[], Task<bool>> copyToClipboard,
        Func<string, Process?> openLens,
        Func<Task> startPasteWatch,
        BrowserPasteInjector pasteInjector,
        PluginLog log)
    {
        _copyToClipboard = copyToClipboard;
        _openLens = openLens;
        _startPasteWatch = startPasteWatch;
        _pasteInjector = pasteInjector;
        _log = log;
    }

    public async Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
    {
        if (!await _copyToClipboard(png).ConfigureAwait(false))
        {
            _log.Warn(nameof(LensPasteProvider), "copying the image to the clipboard failed");
            return VisualSearchOutcome.Fail(LensUploadFailure.ClipboardUnavailable);
        }

        Process? process;
        try
        {
            process = _openLens(LensHomeUrl);
        }
        catch (Exception exception)
        {
            _log.Error(nameof(LensPasteProvider), "opening the browser failed", exception);
            return VisualSearchOutcome.Fail(LensUploadFailure.BrowserLaunchFailed);
        }
        if (process is null)
        {
            _log.Warn(nameof(LensPasteProvider), "browser launch could not be confirmed");
            return VisualSearchOutcome.Fail(LensUploadFailure.BrowserLaunchFailed);
        }

        _pasteInjector.NoteLaunchedBrowser(process);
        process.Dispose();
        _ = _startPasteWatch();
        _log.Info(nameof(LensPasteProvider), "Lens opened; the image is on the clipboard and auto-paste is scheduled");
        return VisualSearchOutcome.Handled();
    }
}
