using System.Text.Json;
using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

public sealed class GoogleAiModeBrowserOperation : IVisualSearchBrowserOperation
{
    private static readonly Uri GoogleAiMode = new("https://www.google.com/search?udm=50");
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan SubmitTimeout = TimeSpan.FromSeconds(30);

    private readonly Task<byte[]> _image;
    private readonly Task<string> _question;
    private readonly Func<byte[], string, IVisualSearchBrowserOperation> _createFallback;
    private readonly PluginLog _log;
    private int _started;

    public GoogleAiModeBrowserOperation(
        Task<byte[]> image,
        Task<string> question,
        Func<byte[], string, IVisualSearchBrowserOperation> createFallback,
        PluginLog log)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _question = question ?? throw new ArgumentNullException(nameof(question));
        _createFallback = createFallback ?? throw new ArgumentNullException(nameof(createFallback));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
        IVisualSearchBrowserSession session,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A Google AI Mode browser operation can only be executed once.");

        byte[] jpeg;
        string question;
        var submitted = false;
        try
        {
            // The page loads while the user is still typing; the image and question arrive later.
            var pageReady = await TryOpenPageAsync(session, cancel).ConfigureAwait(true);
            jpeg = await _image.WaitAsync(cancel).ConfigureAwait(true);
            var attached = pageReady && await TryAttachAsync(session, jpeg, cancel).ConfigureAwait(true);
            question = await _question.WaitAsync(cancel).ConfigureAwait(true);
            if (!attached && pageReady)
            {
                // A traffic check stays hidden until the question reveals the browser; the user can solve it now.
                attached = await GoogleTrafficCheck.WaitForUserAsync(session, cancel).ConfigureAwait(true) &&
                           await TryAttachAsync(session, jpeg, cancel).ConfigureAwait(true);
            }
            submitted = attached && await TrySubmitAsync(session, question, cancel).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return VisualSearchBrowserOperationStatus.Canceled;
        }

        if (submitted)
        {
            _log.Info(nameof(GoogleAiModeBrowserOperation), "Google AI Mode question submitted with the image");
            return VisualSearchBrowserOperationStatus.Succeeded;
        }
        if (cancel.IsCancellationRequested) return VisualSearchBrowserOperationStatus.Canceled;
        _log.Info(nameof(GoogleAiModeBrowserOperation), "falling back to the Google Lens continuation");
        return await _createFallback(jpeg, question).ExecuteAsync(session, cancel).ConfigureAwait(true);
    }

    private async Task<bool> TryOpenPageAsync(IVisualSearchBrowserSession session, CancellationToken cancel)
    {
        var navigation = await session.NavigateAsync(GoogleAiMode, NavigationTimeout, cancel).ConfigureAwait(true);
        if (navigation.Status == BrowserNavigationStatus.Canceled)
            throw new OperationCanceledException(cancel);
        if (navigation.Status == BrowserNavigationStatus.Succeeded) return true;
        _log.Warn(
            nameof(GoogleAiModeBrowserOperation),
            $"Google AI Mode navigation failed: {navigation.Status} {navigation.Error}");
        return false;
    }

    private Task<bool> TryAttachAsync(IVisualSearchBrowserSession session, byte[] jpeg, CancellationToken cancel) =>
        TryBridgeAsync(session, new { type = "attach", image = Convert.ToBase64String(jpeg) },
            "CTS:attached", AttachTimeout, cancel);

    private async Task<bool> TrySubmitAsync(IVisualSearchBrowserSession session, string question, CancellationToken cancel)
    {
        if (await TryBridgeAsync(session, new { type = "send", question }, "CTS:submitted", SubmitTimeout, cancel)
                .ConfigureAwait(true))
        {
            return true;
        }
        // A full page load after sending drops the bridge before it can acknowledge.
        return IsAiModeAnswer(session.CurrentUri);
    }

    private async Task<bool> TryBridgeAsync(
        IVisualSearchBrowserSession session,
        object message,
        string expected,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        if (GoogleTrafficCheck.IsShown(session.CurrentUri)) return false;
        try
        {
            var installed = await session.ExecuteScriptAsync(AskBridgeScript, cancel).ConfigureAwait(true);
            if (!string.Equals(JsonSerializer.Deserialize<string>(installed), "ready", StringComparison.Ordinal))
                return false;
            var acknowledgement = await session.PostWebMessageAndWaitAsync(
                    JsonSerializer.Serialize(message),
                    reply => reply.StartsWith("CTS:", StringComparison.Ordinal),
                    timeout,
                    cancel)
                .ConfigureAwait(true);
            if (acknowledgement is not null &&
                (acknowledgement == expected || acknowledgement.StartsWith(expected + ":", StringComparison.Ordinal)))
            {
                _log.Info(nameof(GoogleAiModeBrowserOperation), $"Google AI Mode page replied {acknowledgement}");
                return true;
            }
            _log.Warn(
                nameof(GoogleAiModeBrowserOperation),
                $"Google AI Mode page did not accept the step: {acknowledgement ?? "timeout"}");
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Warn(
                nameof(GoogleAiModeBrowserOperation),
                $"Google AI Mode page script failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
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
          // An observer keeps counting after the page fills its resource timing buffer.
          let uploads = 0;
          new PerformanceObserver(list => {
            uploads += list.getEntries().filter(entry => entry.name.includes("/crupload")).length;
          }).observe({ type: "resource", buffered: true });
          const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
          const visible = selector => [...document.querySelectorAll(selector)].find(element => element.offsetParent);
          const findInput = async () => {
            for (let attempt = 0; attempt < 100; attempt++) {
              const input = visible("textarea");
              if (input) return input;
              await sleep(100);
            }
            return null;
          };
          const attach = async image => {
            const input = await findInput();
            if (!input) return "input-missing";
            const binary = atob(image);
            const bytes = new Uint8Array(binary.length);
            for (let index = 0; index < binary.length; index++) {
              bytes[index] = binary.charCodeAt(index);
            }
            const file = new File([bytes], "circle-to-search.jpg", { type: "image/jpeg" });
            // The page shows each attachment as a chip titled with its file name, possibly before it is visible.
            const hasChip = () => document.querySelector('[title="circle-to-search.jpg"]') !== null;
            for (let wait = 0; wait < 100 && document.readyState !== "complete"; wait++) await sleep(100);
            // Pasting again duplicates the image, so repeat only when the page shows no sign of taking it.
            for (let pastes = 0; pastes < 6; pastes++) {
              if (hasChip()) return `attached:${pastes}`;
              const uploadsBefore = uploads;
              const transfer = new DataTransfer();
              transfer.items.add(file);
              (visible("textarea") ?? input).dispatchEvent(
                new ClipboardEvent("paste", { clipboardData: transfer, bubbles: true, cancelable: true }));
              for (let wait = 0; wait < 50; wait++) {
                await sleep(100);
                if (hasChip() || uploads > uploadsBefore) return `attached:${pastes + 1}`;
              }
            }
            return "paste-ignored";
          };
          const send = async question => {
            const input = await findInput();
            if (!input) return "input-missing";
            Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value").set.call(input, question);
            input.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: question }));
            // Sending is ignored while the attached image is still uploading, so retry until accepted.
            for (let attempt = 0; attempt < 100; attempt++) {
              if (!input.isConnected || input.value !== question) return "submitted";
              visible('[data-xid="input-plate-send-button"]')?.click();
              await sleep(200);
            }
            return "send-timeout";
          };
          window.chrome.webview.addEventListener("message", async event => {
            const reply = status => window.chrome.webview.postMessage(`CTS:${status}`);
            try {
              const message = JSON.parse(event.data);
              if (message.type === "attach") reply(await attach(message.image));
              else if (message.type === "send") reply(await send(message.question));
            } catch (error) {
              reply("script-error");
            }
          });
          return "ready";
        })()
        """;
}
