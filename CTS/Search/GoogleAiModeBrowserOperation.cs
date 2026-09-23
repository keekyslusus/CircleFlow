using System.Text.Json;
using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

public sealed class GoogleAiModeBrowserOperation : IVisualSearchBrowserOperation
{
    private static readonly Uri GoogleAiMode = new("https://www.google.com/search?udm=50");
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SubmitTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan TrafficCheckTimeout = TimeSpan.FromMinutes(3);

    private readonly string _question;
    private readonly IVisualSearchBrowserOperation _fallback;
    private readonly PluginLog _log;
    private byte[]? _jpeg;
    private int _started;

    public GoogleAiModeBrowserOperation(
        byte[] jpeg,
        string question,
        IVisualSearchBrowserOperation fallback,
        PluginLog log)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        _jpeg = jpeg;
        _question = question;
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
        IVisualSearchBrowserSession session,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A Google AI Mode browser operation can only be executed once.");

        var jpeg = Interlocked.Exchange(ref _jpeg, null)
                  ?? throw new InvalidOperationException("The image is no longer available.");
        try
        {
            if (await TrySubmitAsync(session, jpeg, cancel).ConfigureAwait(true))
            {
                _log.Info(nameof(GoogleAiModeBrowserOperation), "Google AI Mode question submitted with the image");
                return VisualSearchBrowserOperationStatus.Succeeded;
            }
        }
        catch (OperationCanceledException)
        {
            return VisualSearchBrowserOperationStatus.Canceled;
        }
        catch (Exception exception)
        {
            _log.Warn(
                nameof(GoogleAiModeBrowserOperation),
                $"Google AI Mode submission failed: {exception.GetType().Name}: {exception.Message}");
        }

        if (cancel.IsCancellationRequested) return VisualSearchBrowserOperationStatus.Canceled;
        _log.Info(nameof(GoogleAiModeBrowserOperation), "falling back to the Google Lens continuation");
        return await _fallback.ExecuteAsync(session, cancel).ConfigureAwait(true);
    }

    private async Task<bool> TrySubmitAsync(
        IVisualSearchBrowserSession session,
        byte[] jpeg,
        CancellationToken cancel)
    {
        var navigation = await session.NavigateAsync(GoogleAiMode, NavigationTimeout, cancel).ConfigureAwait(true);
        if (navigation.Status == BrowserNavigationStatus.Canceled)
            throw new OperationCanceledException(cancel);
        if (navigation.Status != BrowserNavigationStatus.Succeeded)
        {
            _log.Warn(
                nameof(GoogleAiModeBrowserOperation),
                $"Google AI Mode navigation failed: {navigation.Status} {navigation.Error}");
            return false;
        }
        if (!await PassTrafficCheckAsync(session, cancel).ConfigureAwait(true)) return false;

        var installed = await session.ExecuteScriptAsync(AskBridgeScript, cancel).ConfigureAwait(true);
        if (!string.Equals(JsonSerializer.Deserialize<string>(installed), "ready", StringComparison.Ordinal))
            return false;

        var acknowledgement = await session.PostWebMessageAndWaitAsync(
                JsonSerializer.Serialize(new { image = Convert.ToBase64String(jpeg), question = _question }),
                message => message.StartsWith("CTS:", StringComparison.Ordinal),
                SubmitTimeout,
                cancel)
            .ConfigureAwait(true);
        if (string.Equals(acknowledgement, "CTS:submitted", StringComparison.Ordinal)) return true;
        // A full page load after sending would drop the bridge before it can acknowledge.
        if (acknowledgement is null && IsAiModeAnswer(session.CurrentUri)) return true;

        _log.Warn(
            nameof(GoogleAiModeBrowserOperation),
            $"Google AI Mode did not accept the question: {acknowledgement ?? "timeout"}");
        return false;
    }

    private static async Task<bool> PassTrafficCheckAsync(
        IVisualSearchBrowserSession session,
        CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow + TrafficCheckTimeout;
        while (GoogleLensBrowserOperation.IsGoogleTrafficCheck(session.CurrentUri))
        {
            // The user solves the check in the visible browser, which then returns to AI Mode.
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return false;
            var navigation = await session.WaitForNavigationAsync(remaining, cancel).ConfigureAwait(true);
            if (navigation.Status == BrowserNavigationStatus.Canceled)
                throw new OperationCanceledException(cancel);
            if (navigation.Status == BrowserNavigationStatus.TimedOut) return false;
        }
        return true;
    }

    internal static bool IsAiModeAnswer(Uri? uri)
    {
        if (uri is not { Scheme: "https", Host: "google.com" or "www.google.com", AbsolutePath: "/search" })
            return false;
        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        return parameters.Contains("udm=50", StringComparer.Ordinal) &&
               parameters.Any(parameter => parameter.StartsWith("vsrid=", StringComparison.Ordinal));
    }

    internal const string AskBridgeScript = """
        (() => {
          if (window.__circleToSearchAskInstalled) return "ready";
          window.__circleToSearchAskInstalled = true;
          window.chrome.webview.addEventListener("message", async event => {
            const reply = status => window.chrome.webview.postMessage(`CTS:${status}`);
            const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
            const visible = selector => [...document.querySelectorAll(selector)].find(element => element.offsetParent);
            try {
              const { image, question } = JSON.parse(event.data);
              let input = null;
              for (let attempt = 0; attempt < 100 && !input; attempt++) {
                input = visible("textarea");
                if (!input) await sleep(100);
              }
              if (!input) return reply("input-missing");

              const binary = atob(image);
              const bytes = new Uint8Array(binary.length);
              for (let index = 0; index < binary.length; index++) {
                bytes[index] = binary.charCodeAt(index);
              }
              const transfer = new DataTransfer();
              transfer.items.add(new File([bytes], "circle-to-search.jpg", { type: "image/jpeg" }));
              input.focus();
              const paste = new ClipboardEvent("paste", { clipboardData: transfer, bubbles: true, cancelable: true });
              input.dispatchEvent(paste);
              if (!paste.defaultPrevented) return reply("paste-ignored");

              Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value").set.call(input, question);
              input.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: question }));
              // Sending is ignored while the attached image is still uploading, so retry until accepted.
              for (let attempt = 0; attempt < 125; attempt++) {
                if (!input.isConnected || input.value !== question) return reply("submitted");
                visible('[data-xid="input-plate-send-button"]')?.click();
                await sleep(200);
              }
              reply("send-timeout");
            } catch (error) {
              reply("script-error");
            }
          }, { once: true });
          return "ready";
        })()
        """;
}
