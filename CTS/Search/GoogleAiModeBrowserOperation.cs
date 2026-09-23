using System.Text.Json;
using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

public sealed class GoogleAiModeBrowserOperation : IVisualSearchBrowserOperation
{
    private static readonly Uri GoogleAiMode = new("https://www.google.com/search?udm=50");
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    // Longer than the page script's own worst case, so a timeout means the page stopped answering.
    private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SubmitTimeout = TimeSpan.FromSeconds(40);

    private readonly Task<byte[]> _image;
    private readonly Task<string> _question;
    private readonly Func<byte[], string, IVisualSearchBrowserOperation> _createFallback;
    private readonly PluginLog _log;
    private int _started;
    private int _nextRequestId;

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
            var attach = pageReady ? await AttachAsync(session, jpeg, cancel).ConfigureAwait(true) : null;
            question = await _question.WaitAsync(cancel).ConfigureAwait(true);
            // Another try helps only a page that was still starting or hidden behind a traffic check.
            if (pageReady && !IsAttached(attach) && attach != "input-missing")
            {
                // The revealed browser now lets the user solve a traffic check.
                attach = await GoogleTrafficCheck.WaitForUserAsync(session, cancel).ConfigureAwait(true)
                    ? await AttachAsync(session, jpeg, cancel).ConfigureAwait(true)
                    : null;
            }
            var attached = IsAttached(attach);
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

    private Task<string?> AttachAsync(IVisualSearchBrowserSession session, byte[] jpeg, CancellationToken cancel) =>
        RequestAsync(session, id => new { id, type = "attach", image = Convert.ToBase64String(jpeg) },
            AttachTimeout, cancel);

    private static bool IsAttached(string? status) =>
        status is not null && (status == "attached" || status.StartsWith("attached:", StringComparison.Ordinal));

    private async Task<bool> TrySubmitAsync(IVisualSearchBrowserSession session, string question, CancellationToken cancel)
    {
        var status = await RequestAsync(session, id => new { id, type = "send", question }, SubmitTimeout, cancel)
            .ConfigureAwait(true);
        // A full page load after sending drops the bridge before it can acknowledge.
        return status == "submitted" || IsAiModeAnswer(session.CurrentUri);
    }

    // Returns the page's status for this request, or null when the page could not answer it.
    private async Task<string?> RequestAsync(
        IVisualSearchBrowserSession session,
        Func<int, object> createMessage,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        if (GoogleTrafficCheck.IsShown(session.CurrentUri)) return null;
        // Replies carry the request id so a late answer to an earlier step is never taken for this one.
        var id = Interlocked.Increment(ref _nextRequestId);
        var prefix = $"CTS:{id}:";
        try
        {
            var installed = await session.ExecuteScriptAsync(AskBridgeScript, cancel).ConfigureAwait(true);
            if (!string.Equals(JsonSerializer.Deserialize<string>(installed), "ready", StringComparison.Ordinal))
                return null;
            var reply = await session.PostWebMessageAndWaitAsync(
                    JsonSerializer.Serialize(createMessage(id)),
                    message => message.StartsWith(prefix, StringComparison.Ordinal),
                    timeout,
                    cancel)
                .ConfigureAwait(true);
            var status = reply?[prefix.Length..];
            _log.Info(nameof(GoogleAiModeBrowserOperation), $"Google AI Mode page replied {status ?? "nothing"}");
            return status;
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
            return null;
        }
    }

    internal static bool IsAiModeAnswer(Uri? uri) =>
        GoogleSearchUrl.IsSearch(uri) &&
        GoogleSearchUrl.HasParameter(uri!, "udm", "50") &&
        GoogleSearchUrl.HasParameter(uri!, "vsrid");

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
            // Only an emptied or replaced field or an answer URL proves the page sent the question.
            const sent = () =>
              !input.isConnected || input.value.trim() === "" || /[?&]vsrid=/.test(location.search);
            for (let attempt = 0; attempt < 100; attempt++) {
              if (sent()) return "submitted";
              visible('[data-xid="input-plate-send-button"]')?.click();
              await sleep(200);
            }
            return "send-timeout";
          };
          // Overlapping attach requests share one paste loop instead of pasting the image twice.
          let attaching = null;
          window.chrome.webview.addEventListener("message", async event => {
            let id = null;
            const reply = status => window.chrome.webview.postMessage(`CTS:${id}:${status}`);
            try {
              const message = JSON.parse(event.data);
              id = message.id;
              if (message.type === "attach") {
                attaching ??= attach(message.image).finally(() => { attaching = null; });
                reply(await attaching);
              }
              else if (message.type === "send") reply(await send(message.question));
            } catch (error) {
              reply("script-error");
            }
          });
          return "ready";
        })()
        """;
}
