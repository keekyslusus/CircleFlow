using System.Net;
using CircleToSearch.MusicRecognition.Fingerprinting;
using CircleToSearch.MusicRecognition.Shazam;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ShazamUserAgentTests
{
    [Fact]
    public void Constant_database_contains_the_verified_pixel_user_agents()
    {
        Assert.Equal(152, PixelUserAgentProvider.Profiles.Count);
        Assert.Equal(152, PixelUserAgentProvider.Profiles.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(
            "Dalvik/2.1.0 (Linux; U; Android 17; Pixel 11 Pro XL Build/CD1A.260714.001.A9)",
            PixelUserAgentProvider.Profiles);
        Assert.All(PixelUserAgentProvider.Profiles, userAgent =>
            Assert.StartsWith("Dalvik/2.1.0 (Linux; U; Android ", userAgent, StringComparison.Ordinal));
    }

    [Fact]
    public void Provider_selects_from_the_constant_database()
    {
        var provider = new PixelUserAgentProvider(upperBound => upperBound - 1);

        Assert.Equal(PixelUserAgentProvider.Profiles[^1], provider.Select());
    }

    [Fact]
    public async Task Client_keeps_one_user_agent_for_its_lifetime()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var provider = new SequenceUserAgentProvider("pixel-one", "pixel-two");
        var client = new ShazamClient(httpClient, provider, new SequenceLocationProvider(
            new ShazamLocation(10, 40.7128, -74.0060, "America/New_York")));
        var signature = new ShazamSignature(ShazamSignature.RequiredSampleRate);

        Assert.Equal(1, provider.Calls);
        Assert.Equal("pixel-one", client.UserAgent);
        await client.RecognizeAsync(signature, CancellationToken.None);
        await client.RecognizeAsync(signature, CancellationToken.None);

        Assert.Equal(["pixel-one", "pixel-one"], handler.UserAgents);
        Assert.Equal(1, provider.Calls);
    }

    private sealed class SequenceUserAgentProvider(params string[] userAgents) : IShazamUserAgentProvider
    {
        private readonly Queue<string> _userAgents = new(userAgents);

        public int Calls { get; private set; }

        public string Select()
        {
            Calls++;
            return _userAgents.Dequeue();
        }
    }

    private sealed class SequenceLocationProvider(params ShazamLocation[] locations) : IShazamLocationProvider
    {
        private readonly Queue<ShazamLocation> _locations = new(locations);

        public ShazamLocation Select() => _locations.Dequeue();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UserAgents.Add(request.Headers.GetValues("User-Agent").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
        }
    }
}
