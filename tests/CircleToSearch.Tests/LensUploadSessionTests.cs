using System.Net;
using System.Net.Http;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class LensUploadSessionTests
{
    [Fact]
    public async Task Session_cookies_are_sent_with_the_upload()
    {
        var handler = new CountingHandler(_ => Found());
        var manager = NewManager(farmReturnsSession: true, out var state);
        var provider = new GoogleLensProvider(handler, sessions: manager, log: SilentLog());

        var outcome = await provider.SearchAsync([1, 2, 3], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(1, state.Farms);
        Assert.Contains("NID=n1", handler.LastCookieHeader);
        Assert.Contains("AEC=a1", handler.LastCookieHeader);
    }

    [Fact]
    public async Task Unexpected_status_triggers_one_refresh_and_retry()
    {
        var handler = new CountingHandler(callIndex => callIndex == 0
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : Found());
        var manager = NewManager(farmReturnsSession: true, out var state);
        var provider = new GoogleLensProvider(handler, sessions: manager, log: SilentLog());

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(2, state.Farms);
        Assert.Equal(2, handler.Calls);
        Assert.Contains("NID=n2", handler.LastCookieHeader);
    }

    [Fact]
    public async Task Fresh_cached_session_is_reused_without_refarming()
    {
        var handler = new CountingHandler(_ => Found());
        var manager = NewManager(farmReturnsSession: true, out var state);
        await manager.GetOrFarmAsync(force: false);
        state.Farms = 0;
        var provider = new GoogleLensProvider(handler, sessions: manager, log: SilentLog());

        await provider.SearchAsync([1], CancellationToken.None);

        Assert.Equal(0, state.Farms);
    }

    [Fact]
    public async Task Failed_farming_reports_failure_without_throwing()
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var manager = NewManager(farmReturnsSession: false, out var state);
        var provider = new GoogleLensProvider(handler, sessions: manager, log: SilentLog());

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(LensUploadFailure.UnexpectedStatus, outcome.Failure);
        Assert.Equal(2, state.Farms);
    }

    private static LensSessionManager NewManager(bool farmReturnsSession, out SessionState state)
    {
        state = new SessionState();
        var s = state;
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", "session-manager");
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);

        return new LensSessionManager(
            new LensSessionStore(directory),
            () =>
            {
                s.Farms++;
                s.Version++;
                return Task.FromResult<LensSession?>(farmReturnsSession
                    ? new LensSession
                    {
                        IssuedAt = DateTimeOffset.UtcNow,
                        Cookies = [new LensCookie("NID", $"n{s.Version}", ".google.com", "/"), new LensCookie("AEC", "a1", ".google.com", "/")],
                    }
                    : null);
            },
            SilentLog());
    }

    private static HttpResponseMessage Found()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://lens.google.com/results?udm=26");
        return response;
    }

    private static PluginLog SilentLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests");
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class SessionState
    {
        public int Farms { get; set; }

        public int Version { get; set; }
    }

    private sealed class CountingHandler(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastCookieHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Calls++;
            LastCookieHeader = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : null;
            return Task.FromResult(responder(index));
        }
    }
}
