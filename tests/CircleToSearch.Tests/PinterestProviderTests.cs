using System.Net;
using System.Net.Http;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class PinterestProviderTests
{
    [Fact]
    public void Parses_supplied_response()
    {
        var pins = PinterestProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory, "tests", "CircleToSearch.Tests", "Fixtures", "pinterest.json")));
        Assert.Equal(3, pins.Count);
        Assert.Equal("https://www.pinterest.com/pin/683491680974453312/", pins[0].PinUrl);
        Assert.Equal("", pins[0].Domain);
        Assert.Null(pins[0].Link);
        Assert.Equal("#ff7d00 · Hex · Color Palette", pins[1].Title);
        Assert.Equal("kidspattern.com", pins[1].Domain);
        Assert.Equal("kidspattern.com", pins[1].Link!.Host);
        Assert.Equal("i.pinimg.com", pins[1].Image.Host);
        Assert.Equal((474, 474), (pins[1].Width, pins[1].Height));
    }

    [Fact]
    public void Skips_untrusted_and_duplicate_pins_and_falls_back_to_description()
    {
        var pins = PinterestProvider.Parse("""
            {"status":"success","data":[
             {"id":"1","type":"pin","image_medium_url":"https://evil.test/a.jpg"},
             {"id":"../2","type":"pin","image_medium_url":"https://i.pinimg.com/474x/a.jpg"},
             {"id":"3","type":"board","image_medium_url":"https://i.pinimg.com/474x/a.jpg"},
             {"id":"4","type":"pin","title":" ","description":"  Warm\n tones ","domain":"x.test","link":"javascript:alert(1)",
              "image_large_url":"https://i.pinimg.com/1200x/b.jpg","image_medium_size_pixels":{"width":"474"}},
             {"id":"4","type":"pin","image_medium_url":"https://i.pinimg.com/474x/c.jpg"}]}
            """);
        var pin = Assert.Single(pins);
        Assert.Equal("4", pin.Id);
        Assert.Equal("Warm tones", pin.Title);
        Assert.Equal("", pin.Domain);
        Assert.Null(pin.Link);
        Assert.Equal("https://i.pinimg.com/1200x/b.jpg", pin.Image.AbsoluteUri);
        Assert.Equal((0, 0), (pin.Width, pin.Height));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"failure\",\"data\":[]}")]
    [InlineData("{\"status\":\"success\",\"data\":{}}")]
    public async Task Malformed_response_is_reported(string json)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) })));
        var outcome = await new PinterestProvider(client).PrepareAsync([1], CancellationToken.None);
        Assert.Equal(UploadFailure.BadResponse, outcome.Failure);
    }

    [Fact]
    public async Task Puts_jpeg_as_whole_image_form_and_preserves_empty_result()
    {
        using var client = new HttpClient(new Handler(async (request, cancel) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("https://api.pinterest.com/v3/visual_search/extension/image/", request.RequestUri!.AbsoluteUri);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = form.ToDictionary(part => part.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("image/jpeg", parts["image"].Headers.ContentType!.MediaType);
            Assert.Equal(new byte[] { 1, 2, 3 }, await parts["image"].ReadAsByteArrayAsync(cancel));
            Assert.Equal(["0", "0", "1", "1", "https"], await Task.WhenAll(
                new[] { "x", "y", "w", "h", "base_scheme" }.Select(name => parts[name].ReadAsStringAsync(cancel))));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"success\",\"data\":[]}") };
        }));
        var outcome = await new PinterestProvider(client).PrepareAsync([1, 2, 3], CancellationToken.None);
        Assert.True(outcome.Success);
        Assert.Equal(PreparedVisualSearchKind.Pinterest, outcome.PreparedSearch!.Kind);
        Assert.Empty(outcome.PreparedSearch.PinterestPins);
    }

    [Fact]
    public async Task Cancellation_is_distinguished_from_timeout()
    {
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        }));
        using var cancellation = new CancellationTokenSource();
        var task = new PinterestProvider(client).PrepareAsync([1], cancellation.Token);
        cancellation.Cancel();
        Assert.Equal(UploadFailure.Canceled, (await task).Failure);
    }

    [Fact]
    public async Task Rejected_request_keeps_status_code()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest))));
        var outcome = await new PinterestProvider(client).PrepareAsync([1], CancellationToken.None);
        Assert.Equal(400, outcome.StatusCode);
        Assert.Equal(UploadFailure.UnexpectedStatus, outcome.Failure);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
