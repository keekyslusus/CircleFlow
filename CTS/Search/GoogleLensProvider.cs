using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace CircleToSearch.Search;

public sealed class GoogleLensProvider : IVisualSearchProvider
{
    public const string UploadUrl = "https://lens.google.com/v3/upload";

    private readonly HttpClient _client;
    private readonly LensSessionManager? _sessions;
    private readonly PluginLog? _log;

    public GoogleLensProvider()
        : this(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
    }

    public GoogleLensProvider(LensSessionManager? sessions, PluginLog? log)
        : this(new SocketsHttpHandler { AllowAutoRedirect = false }, sessions: sessions, log: log)
    {
    }

    public GoogleLensProvider(HttpMessageHandler handler, TimeSpan? timeout = null, LensSessionManager? sessions = null, PluginLog? log = null)
    {
        _client = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        _sessions = sessions;
        _log = log;
    }

    public async Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
    {
        var outcome = await UploadOnceAsync(png, cancel).ConfigureAwait(false);

        // A stale session degrades the results silently (the HTTP response still looks like
        // success or an ordinary refusal), so any suspicious outcome is worth one attempt with a
        // freshly farmed session.
        var worthRefreshing = outcome.Failure
            is LensUploadFailure.UnexpectedStatus or LensUploadFailure.EmptyLocation or LensUploadFailure.PolicyRejection;
        if (worthRefreshing && _sessions is not null && await _sessions.GetOrFarmAsync(force: true).ConfigureAwait(false) is not null)
        {
            _log?.Info(nameof(GoogleLensProvider), "session refreshed; retrying the upload once");
            outcome = await UploadOnceAsync(png, cancel).ConfigureAwait(false);
        }
        return outcome;
    }

    private async Task<VisualSearchOutcome> UploadOnceAsync(byte[] png, CancellationToken cancel)
    {
        using var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "encoded_image", "capture.png");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{UploadUrl}?ep=gsbubb&st={DateTimeOffset.Now.ToUnixTimeMilliseconds()}&authuser=0&hl=ru&vpw=1200&vph=900")
        { Content = content };
        request.Headers.Referrer = new Uri("https://www.google.com/");

        LensSession? session = null;
        if (_sessions is not null)
        {
            session = await _sessions.GetOrFarmAsync(force: false).ConfigureAwait(false);
            if (session is null)
                _log?.Warn(nameof(GoogleLensProvider), "no session available; uploading without it (results may be degraded)");
        }
        var cookieHeader = session?.CookieHeader() ?? "";
        if (cookieHeader.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        try
        {
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel)
                .ConfigureAwait(false);

            if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.SeeOther))
                return VisualSearchOutcome.Fail(LensUploadFailure.UnexpectedStatus, (int)response.StatusCode);

            var location = response.Headers.Location;
            if (location is null)
                return VisualSearchOutcome.Fail(LensUploadFailure.EmptyLocation, (int)response.StatusCode);
            if (!RedirectUrlPolicy.IsAllowed(location))
                return VisualSearchOutcome.Fail(LensUploadFailure.PolicyRejection, (int)response.StatusCode);

            return VisualSearchOutcome.Ok(location.AbsoluteUri);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return VisualSearchOutcome.Fail(LensUploadFailure.Canceled);
        }
        catch (OperationCanceledException)
        {
            return VisualSearchOutcome.Fail(LensUploadFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return VisualSearchOutcome.Fail(LensUploadFailure.NetworkError);
        }
    }
}
