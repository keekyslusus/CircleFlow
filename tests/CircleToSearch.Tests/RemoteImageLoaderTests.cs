using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class RemoteImageLoaderTests
{
    [Fact]
    public void Decodes_a_downloaded_image_frozen_at_the_requested_width() => OnSta(() =>
    {
        var images = TestRemoteImages.Serving(TestRemoteImages.Png(400, 200));

        var image = Load(images.LoadAsync(new Uri("https://images.test/cover.png"), 100));

        Assert.NotNull(image);
        Assert.True(image.IsFrozen);
        Assert.Equal(100, image.PixelWidth);
        Assert.Equal(50, image.PixelHeight);
    });

    [Fact]
    public void Keeps_color_management_unless_the_caller_opts_out() => OnSta(() =>
    {
        var images = TestRemoteImages.Serving(TestRemoteImages.Png(8, 8));

        var managed = Assert.IsType<BitmapImage>(Load(images.LoadAsync(new Uri("https://images.test/full.png"))));
        var preview = Assert.IsType<BitmapImage>(Load(images.LoadAsync(new Uri("https://images.test/preview.png"), 4,
            BitmapCreateOptions.IgnoreColorProfile)));

        Assert.Equal(BitmapCreateOptions.None, managed.CreateOptions);
        Assert.Equal(BitmapCreateOptions.IgnoreColorProfile, preview.CreateOptions);
    });

    [Fact]
    public void Returns_null_when_the_download_fails_or_is_not_an_image() => OnSta(() =>
    {
        Assert.Null(Load(TestRemoteImages.Offline.LoadAsync(new Uri("https://images.test/missing.png"))));
        Assert.Null(Load(TestRemoteImages.Serving(Encoding.UTF8.GetBytes("<html></html>"))
            .LoadAsync(new Uri("https://images.test/page.png"))));
    });

    [Fact]
    public void Returns_null_for_an_image_over_the_size_limit() => OnSta(() =>
    {
        var png = TestRemoteImages.Png(64, 64);
        var images = new RemoteImageLoader(new HttpClient(TestRemoteImages.Responding(_ => png)), maxBytes: png.Length - 1);

        Assert.Null(Load(images.LoadAsync(new Uri("https://images.test/large.png"))));
    });

    [Fact]
    public void Returns_null_once_the_client_is_disposed_at_shutdown() => OnSta(() =>
    {
        var client = new HttpClient(TestRemoteImages.Responding(_ => TestRemoteImages.Png(8, 8)));
        var images = new RemoteImageLoader(client);
        client.Dispose();

        Assert.Null(Load(images.LoadAsync(new Uri("https://images.test/late.png"))));
    });

    [Fact]
    public void Sends_a_user_agent() => OnSta(() =>
    {
        string? userAgent = null;
        var images = new RemoteImageLoader(new HttpClient(TestRemoteImages.Responding(request =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return TestRemoteImages.Png(8, 8);
        })));

        Assert.NotNull(Load(images.LoadAsync(new Uri("https://images.test/cover.png"))));
        Assert.StartsWith("CircleFlow/", userAgent);
    });

    // WPF's own download keeps a COM object from the first thread that used it; overlay threads exit after each session.
    [Fact]
    public void Production_code_does_not_let_wpf_download_images()
    {
        var wpfDownload = new Regex(@"\.UriSource\s*=|new BitmapImage\(\s*[A-Za-z_]", RegexOptions.CultureInvariant);
        var violations = Directory
            .EnumerateFiles(Path.Combine(TestOutputPaths.RepoDirectory, "CTS"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, number = index + 1 }))
            .Where(item => wpfDownload.IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(TestOutputPaths.RepoDirectory, item.path)}:{item.number}")
            .ToArray();

        Assert.True(violations.Length == 0, $"Load remote images with RemoteImageLoader: {string.Join(", ", violations)}");
    }

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
}
