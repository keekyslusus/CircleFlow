using CircleToSearch.Search;
using System.Text;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensWindowTests
{
    [Fact]
    public async Task Show_returns_failed_when_dispatcher_rejects_post()
    {
        var dispatcher = new TestStaDispatcher { TryPostResult = false };
        using var window = CreateWindow(dispatcher);

        var result = await window.ShowAsync([1, 2, 3], CancellationToken.None);

        Assert.Equal(GoogleLensSearchStatus.Failed, result);
        Assert.Equal(1, dispatcher.TryPostCalls);
    }

    [Fact]
    public void Repeated_dispose_sends_cleanup_and_disposes_dispatcher_once()
    {
        var dispatcher = new TestStaDispatcher();
        var window = CreateWindow(dispatcher);

        window.Dispose();
        window.Dispose();

        Assert.Equal(1, dispatcher.SendCalls);
        Assert.Equal(1, dispatcher.DisposeCalls);
    }

    [Fact]
    public void Composition_root_preserves_webview_thread_name()
    {
        Assert.Equal("CircleToSearch WebView2", CompositionRoot.GoogleLensThreadName);
    }

    [Fact]
    public void Direct_upload_builds_the_expected_raw_jpeg_multipart_request()
    {
        const string boundary = "----CircleToSearchTestBoundary";
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0xFF, 0x0D, 0x0A];

        using var body = GoogleLensWindow.CreateLensUploadBody(jpeg, boundary);

        var prefix = Encoding.ASCII.GetBytes(
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"encoded_image\"; filename=\"circle-to-search.jpg\"\r\n" +
            "Content-Type: image/jpeg\r\n\r\n");
        var suffix = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
        Assert.Equal(prefix.Concat(jpeg).Concat(suffix), body.ToArray());
        Assert.Equal(0, body.Position);
        Assert.Equal(
            $"Content-Type: multipart/form-data; boundary={boundary}",
            GoogleLensWindow.CreateLensUploadHeaders(boundary));
    }

    [Theory]
    [InlineData("bad\rboundary")]
    [InlineData("bad\nboundary")]
    public void Direct_upload_rejects_a_boundary_with_line_breaks(string boundary)
    {
        Assert.Throws<ArgumentException>(() =>
            GoogleLensWindow.CreateLensUploadBody([1, 2, 3], boundary));
    }

    private static GoogleLensWindow CreateWindow(TestStaDispatcher dispatcher)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CircleToSearch.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new GoogleLensWindow(
            AppContext.BaseDirectory,
            Path.Combine(directory, "Profile"),
            TestUiStrings.English,
            new PluginLog(directory),
            dispatcher);
    }
}
