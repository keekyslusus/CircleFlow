using System.IO;
using System.Text;
using System.Text.Json;
using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

public sealed class GoogleLensBrowserOperation : IVisualSearchBrowserOperation
{
    private static readonly Uri GoogleLensHome = new("https://lens.google.com/");
    private static readonly Uri GoogleLensUpload = new("https://lens.google.com/v3/upload");
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AttachmentTimeout = TimeSpan.FromSeconds(15);

    private readonly PluginLog _log;
    private byte[]? _jpeg;
    private int _started;

    public GoogleLensBrowserOperation(byte[] jpeg, PluginLog log)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        _jpeg = jpeg;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
        IVisualSearchBrowserSession session,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A Google Lens browser operation can only be executed once.");

        var jpeg = Interlocked.Exchange(ref _jpeg, null)
                  ?? throw new InvalidOperationException("The image is no longer available.");
        try
        {
            cancel.ThrowIfCancellationRequested();
            if (await TryDirectUploadAsync(session, jpeg, cancel).ConfigureAwait(true))
                return VisualSearchBrowserOperationStatus.Succeeded;

            cancel.ThrowIfCancellationRequested();
            _log.Info(nameof(GoogleLensBrowserOperation), "falling back to Google Lens page upload");
            var home = await session.NavigateAsync(GoogleLensHome, NavigationTimeout, cancel)
                .ConfigureAwait(true);
            if (home.Status == BrowserNavigationStatus.Canceled)
                return VisualSearchBrowserOperationStatus.Canceled;
            if (home.Status != BrowserNavigationStatus.Succeeded)
                return VisualSearchBrowserOperationStatus.Failed;

            return await TryPageUploadAsync(session, jpeg, cancel).ConfigureAwait(true)
                ? VisualSearchBrowserOperationStatus.Succeeded
                : VisualSearchBrowserOperationStatus.Failed;
        }
        catch (OperationCanceledException)
        {
            return VisualSearchBrowserOperationStatus.Canceled;
        }
    }

    private async Task<bool> TryDirectUploadAsync(
        IVisualSearchBrowserSession session,
        byte[] jpeg,
        CancellationToken cancel)
    {
        try
        {
            var boundary = $"----CircleToSearch{Guid.NewGuid():N}";
            using var body = CreateLensUploadBody(jpeg, boundary);
            var navigation = await session.NavigatePostAsync(
                    GoogleLensUpload,
                    body,
                    CreateLensUploadHeaders(boundary),
                    NavigationTimeout,
                    cancel)
                .ConfigureAwait(true);
            if (navigation.Status == BrowserNavigationStatus.Canceled)
                throw new OperationCanceledException(cancel);
            if (navigation.Status != BrowserNavigationStatus.Succeeded)
            {
                _log.Warn(
                    nameof(GoogleLensBrowserOperation),
                    $"Google Lens direct upload navigation failed: {navigation.Status} {navigation.Error}");
                return false;
            }

            if (!IsGoogleLensResultsUrl(session.CurrentUri))
            {
                _log.Warn(
                    nameof(GoogleLensBrowserOperation),
                    "Google Lens direct upload returned an unexpected results location");
                return false;
            }

            _log.Info(nameof(GoogleLensBrowserOperation), "Google Lens results opened through direct upload");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Warn(
                nameof(GoogleLensBrowserOperation),
                $"Google Lens direct upload failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private async Task<bool> TryPageUploadAsync(
        IVisualSearchBrowserSession session,
        byte[] jpeg,
        CancellationToken cancel)
    {
        var installed = await session.ExecuteScriptAsync(AttachmentBridgeScript, cancel)
            .ConfigureAwait(true);
        if (!string.Equals(JsonSerializer.Deserialize<string>(installed), "ready", StringComparison.Ordinal))
            return false;

        using var navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var navigation = session.WaitForNavigationAsync(
            AttachmentTimeout + NavigationTimeout,
            navigationCancellation.Token);
        try
        {
            var acknowledgement = await session.PostWebMessageAndWaitAsync(
                    Convert.ToBase64String(jpeg),
                    message => message.StartsWith("CTS:", StringComparison.Ordinal),
                    AttachmentTimeout,
                    cancel)
                .ConfigureAwait(true);
            if (!string.Equals(acknowledgement, "CTS:submitted", StringComparison.Ordinal))
            {
                _log.Warn(nameof(GoogleLensBrowserOperation), "Google Lens upload was not acknowledged");
                return false;
            }

            var result = await navigation.ConfigureAwait(true);
            if (result.Status == BrowserNavigationStatus.Canceled)
                throw new OperationCanceledException(cancel);
            if (result.Status != BrowserNavigationStatus.Succeeded)
            {
                _log.Warn(
                    nameof(GoogleLensBrowserOperation),
                    $"Google Lens results navigation failed: {result.Status} {result.Error}");
                return false;
            }

            if (!IsGoogleLensResultsUrl(session.CurrentUri))
            {
                _log.Warn(
                    nameof(GoogleLensBrowserOperation),
                    "Google Lens page upload returned an unexpected results location");
                return false;
            }

            _log.Info(nameof(GoogleLensBrowserOperation), "Google Lens results opened through page upload");
            return true;
        }
        finally
        {
            navigationCancellation.Cancel();
            await navigation.ConfigureAwait(true);
        }
    }

    internal static MemoryStream CreateLensUploadBody(byte[] jpeg, string boundary)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        if (boundary.Contains('\r', StringComparison.Ordinal) ||
            boundary.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("Multipart boundary cannot contain line breaks.", nameof(boundary));
        }

        var stream = new MemoryStream();
        WriteAscii(
            stream,
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"encoded_image\"; filename=\"circle-to-search.jpg\"\r\n" +
            "Content-Type: image/jpeg\r\n\r\n");
        stream.Write(jpeg);
        WriteAscii(stream, $"\r\n--{boundary}--\r\n");
        stream.Position = 0;
        return stream;
    }

    internal static string CreateLensUploadHeaders(string boundary) =>
        $"Content-Type: multipart/form-data; boundary={boundary}";

    internal static bool IsGoogleLensResultsUrl(Uri? uri)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.Host == "lens.google.com")
            return uri.AbsolutePath.StartsWith("/search", StringComparison.Ordinal);

        if (uri.Host is not ("google.com" or "www.google.com") || uri.AbsolutePath != "/search")
            return false;

        return uri.Query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part is "?udm=26" or "udm=26");
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }

    internal const string AttachmentBridgeScript = """
        (() => {
          if (window.__circleToSearchBridgeInstalled) return "ready";
          window.__circleToSearchBridgeInstalled = true;
          window.chrome.webview.addEventListener("message", async event => {
            const reply = status => window.chrome.webview.postMessage(`CTS:${status}`);
            const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
            try {
              let dropArea = null;
              for (let attempt = 0; attempt < 100 && !dropArea; attempt++) {
                const candidate = document.querySelector('div[data-ved][jsname="QdEQIc"]');
                if (candidate?.offsetParent) dropArea = candidate;
                if (!dropArea) await sleep(100);
              }
              if (!dropArea) return reply("drop-area-missing");

              const binary = atob(event.data);
              const bytes = new Uint8Array(binary.length);
              for (let index = 0; index < binary.length; index++) {
                bytes[index] = binary.charCodeAt(index);
              }
              const fileName = "circle-to-search.jpg";
              const transfer = new DataTransfer();
              transfer.items.add(new File([bytes], fileName, { type: "image/jpeg" }));

              for (const type of ["dragenter", "dragover", "drop"]) {
                dropArea.dispatchEvent(new DragEvent(type, {
                  bubbles: true,
                  cancelable: true,
                  dataTransfer: transfer
                }));
              }
              reply("submitted");
            } catch (error) {
              reply("script-error");
            }
          }, { once: true });
          return "ready";
        })()
        """;
}
