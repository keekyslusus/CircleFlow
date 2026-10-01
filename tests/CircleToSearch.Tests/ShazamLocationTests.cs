using System.Net;
using System.Text.Json;
using CircleToSearch.MusicRecognition.Fingerprinting;
using CircleToSearch.MusicRecognition.Shazam;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ShazamLocationTests
{
    [Fact]
    public void Profiles_have_valid_coordinates_and_supported_US_timezones()
    {
        var supportedTimezones = new HashSet<string>(StringComparer.Ordinal)
        {
            "America/New_York",
            "America/Detroit",
            "America/Chicago",
            "America/Denver",
            "America/Boise",
            "America/Phoenix",
            "America/Los_Angeles",
            "America/Anchorage",
            "Pacific/Honolulu",
        };

        Assert.NotEmpty(ShazamLocationProvider.Profiles);
        Assert.Equal(ShazamLocationProvider.Profiles.Count, ShazamLocationProvider.Profiles.Distinct().Count());
        Assert.DoesNotContain(ShazamLocationProvider.Profiles, location =>
            location.Latitude == 45 && location.Longitude == 2 && location.Timezone == "Europe/Paris");
        Assert.All(ShazamLocationProvider.Profiles, location =>
        {
            Assert.InRange(location.Altitude, -100, 3000);
            Assert.InRange(location.Latitude, -90, 90);
            Assert.InRange(location.Longitude, -180, 180);
            Assert.Contains(location.Timezone, supportedTimezones);
        });
    }

    [Fact]
    public async Task Client_keeps_one_matching_location_and_timezone_for_its_lifetime()
    {
        var selected = new ShazamLocation(1609, 39.7392, -104.9903, "America/Denver");
        var unused = new ShazamLocation(5, 21.3099, -157.8581, "Pacific/Honolulu");
        var locations = new SequenceLocationProvider(selected, unused);
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ShazamClient(httpClient, new ConstantUserAgentProvider(), locations);
        var signature = new ShazamSignature(ShazamSignature.RequiredSampleRate);

        Assert.Equal(selected, client.Location);
        await client.RecognizeAsync(signature, CancellationToken.None);
        await client.RecognizeAsync(signature, CancellationToken.None);

        Assert.Equal(1, locations.Calls);
        Assert.Equal(2, handler.Payloads.Count);
        Assert.All(handler.Payloads, payload =>
        {
            var geolocation = payload.GetProperty("geolocation");
            Assert.Equal(selected.Altitude, geolocation.GetProperty("altitude").GetDouble());
            Assert.Equal(selected.Latitude, geolocation.GetProperty("latitude").GetDouble());
            Assert.Equal(selected.Longitude, geolocation.GetProperty("longitude").GetDouble());
            Assert.Equal(selected.Timezone, payload.GetProperty("timezone").GetString());
        });
    }

    private sealed class ConstantUserAgentProvider : IShazamUserAgentProvider
    {
        public string Select() => "pixel";
    }

    private sealed class SequenceLocationProvider(params ShazamLocation[] locations) : IShazamLocationProvider
    {
        private readonly Queue<ShazamLocation> _locations = new(locations);

        public int Calls { get; private set; }

        public ShazamLocation Select()
        {
            Calls++;
            return _locations.Dequeue();
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<JsonElement> Payloads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await using var stream = await request.Content!.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            Payloads.Add(document.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };
        }
    }
}
