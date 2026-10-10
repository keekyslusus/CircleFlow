using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class PinterestSimulationTests
{
    [Fact]
    public async Task Simulated_search_returns_pins_whose_images_load_without_the_network()
    {
        var network = new RecordingNetwork();
        var simulation = Simulation();
        simulation.Mode = PinterestDebugMode.Simulated;
        using var client = new HttpClient(simulation.CreateHandler(network));

        var outcome = await new PinterestProvider(client).PrepareAsync([1, 2, 3], CancellationToken.None);

        var pins = outcome.PreparedSearch!.PinterestPins;
        Assert.Equal(PinterestSimulation.PinCount, pins.Count);
        Assert.Equal(TestUiStrings.English.DebugPinterestPinTitle(1), pins[0].Title);
        Assert.All(pins, pin =>
        {
            Assert.Equal("i.pinimg.com", pin.Image.Host);
            Assert.Equal(474, pin.Width);
            Assert.True(pin.Height > 0);
        });
        Assert.Contains(pins, pin => pin.Height > pin.Width);
        Assert.Contains(pins, pin => pin.Height < pin.Width);

        OnSta(() =>
        {
            var images = new RemoteImageLoader(client);
            var preview = Load(images.LoadAsync(pins[0].Image));
            var full = Load(images.LoadAsync(pins[0].FullImage));
            Assert.Equal((474, pins[0].Height), (preview!.PixelWidth, preview.PixelHeight));
            Assert.Equal(1200, full!.PixelWidth);
        });
        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task Live_mode_and_other_requests_still_reach_the_network()
    {
        var network = new RecordingNetwork();
        var simulation = Simulation();
        using var client = new HttpClient(simulation.CreateHandler(network));

        var live = await new PinterestProvider(client).PrepareAsync([1], CancellationToken.None);
        Assert.Equal(UploadFailure.UnexpectedStatus, live.Failure);
        Assert.Equal(PinterestProvider.Endpoint, Assert.Single(network.Requests).AbsoluteUri);

        simulation.Mode = PinterestDebugMode.Simulated;
        using var other = await client.GetAsync("https://api.trace.moe/search");
        Assert.Equal("api.trace.moe", network.Requests[^1].Host);
        OnSta(() => Assert.Null(Load(new RemoteImageLoader(client).LoadAsync(new Uri("https://i.pinimg.com/simulated/474/999.jpg")))));
        Assert.Equal(2, network.Requests.Count);
    }

    private static PinterestSimulation Simulation() =>
        new(TestUiStrings.English, searchDelay: TimeSpan.Zero, maxImageDelay: TimeSpan.Zero);

    // The loader decodes on the calling thread's dispatcher, so the test pumps one until the load completes.
    private static BitmapSource? Load(Task<BitmapSource?> load)
    {
        var frame = new DispatcherFrame();
        _ = load.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        var timeout = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Send, (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timeout.Stop();
        Assert.True(load.IsCompletedSuccessfully);
        return load.Result;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    private sealed class RecordingNetwork : HttpMessageHandler
    {
        internal List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request });
        }
    }
}
