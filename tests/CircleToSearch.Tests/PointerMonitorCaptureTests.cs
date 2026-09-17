using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Search;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class PointerMonitorCaptureTests
{
    [Fact]
    public async Task Session_factory_returns_null_when_capture_fails()
    {
        var directory = TestOutputPaths.NewTempDirectory(nameof(Session_factory_returns_null_when_capture_fails));
        var factory = new OverlaySessionFactory(
            new PluginLog(directory),
            new StubCapture(() => null),
            WindowFactory());

        var session = await factory.OpenAsync(LaunchOptions(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(session);
    }

    [Fact]
    public async Task Session_factory_disposes_frame_when_canceled_after_capture()
    {
        var directory = TestOutputPaths.NewTempDirectory(nameof(Session_factory_disposes_frame_when_canceled_after_capture));
        using var cancellation = new CancellationTokenSource();
        var frame = new GdiBitmap(64, 48);
        var factory = new OverlaySessionFactory(
            new PluginLog(directory),
            new StubCapture(() =>
            {
                cancellation.Cancel();
                return new PointerMonitorCaptureResult(
                    new GdiRectangle(0, 0, 64, 48),
                    new GdiRectangle(0, 0, 64, 40),
                    1,
                    new GdiPoint(10, 10),
                    frame);
            }),
            WindowFactory());

        var session = await factory.OpenAsync(LaunchOptions(), cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(session);
        Assert.Throws<ArgumentException>(() => frame.GetHbitmap());
    }

    [Fact]
    public void Returns_null_when_monitor_metadata_is_unavailable()
    {
        var capture = new PointerMonitorCapture(
            () => null,
            static (_, _) => throw new InvalidOperationException("frame should not be created"),
            static (_, _) => throw new InvalidOperationException("screen should not be copied"));

        Assert.Null(capture.Capture());
    }

    [Fact]
    public void Preserves_negative_monitor_metadata_and_pointer()
    {
        var monitor = new GdiRectangle(-1920, -80, 1280, 720);
        var workArea = new GdiRectangle(-1920, -80, 1280, 680);
        var pointer = new GdiPoint(-400, 120);
        var capture = new PointerMonitorCapture(
            () => new PointerMonitorMetadata(monitor, workArea, 1.25, pointer),
            static (width, height) => new GdiBitmap(width, height),
            static (_, _) => { });

        var result = Assert.IsType<PointerMonitorCaptureResult>(capture.Capture());
        using (result.Frame)
        {
            Assert.Equal(monitor, result.Monitor);
            Assert.Equal(workArea, result.WorkArea);
            Assert.Equal(1.25, result.Scale);
            Assert.Equal(pointer, result.Pointer);
        }
    }

    [Fact]
    public void Disposes_created_frame_when_copy_throws()
    {
        GdiBitmap? created = null;
        var capture = new PointerMonitorCapture(
            () => new PointerMonitorMetadata(
                new GdiRectangle(0, 0, 64, 48),
                new GdiRectangle(0, 0, 64, 40),
                1,
                new GdiPoint(10, 10)),
            (width, height) => created = new GdiBitmap(width, height),
            static (_, _) => throw new InvalidOperationException("copy failed"));

        var failure = Assert.Throws<InvalidOperationException>(() => capture.Capture());

        Assert.Equal("copy failed", failure.Message);
        Assert.NotNull(created);
        Assert.Throws<ArgumentException>(() => created.GetHbitmap());
    }

    private static OverlayLaunchOptions LaunchOptions() => new(
        new OverlayOptions(8, 12),
        TestUiStrings.English,
        [new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens")],
        SearchProviderIds.GoogleLens);

    private static IOverlayWindowFactory WindowFactory() =>
        new OverlayWindowFactory(TestOverlayControllers.CreateFactory());

    private sealed class StubCapture(Func<PointerMonitorCaptureResult?> capture) : IPointerMonitorCapture
    {
        public PointerMonitorCaptureResult? Capture() => capture();
    }
}
