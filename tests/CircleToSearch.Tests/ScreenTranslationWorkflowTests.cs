using System.Net;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ScreenTranslationWorkflowTests
{
    [Fact]
    public async Task Unexpected_failure_is_safe_to_log_and_maps_to_bad_response()
    {
        const string secret = "secret OCR payload 7462";
        var directory = NewDirectory();
        var workflow = new ScreenTranslationWorkflow(
            new ThrowingProvider(new InvalidOperationException(secret)),
            new PluginLog(directory));

        var outcome = await workflow.TranslateAsync(Guid.NewGuid(), Source(), "en", CancellationToken.None);
        var contents = File.ReadAllText(Path.Combine(directory, "plugin.log"));

        Assert.Equal(TranslationFailure.BadResponse, outcome.Failure);
        Assert.Contains(typeof(InvalidOperationException).FullName!, contents);
        Assert.Contains("hresult=", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(ThrowingProvider.TranslateAsync), contents);
        Assert.DoesNotContain(secret, contents);
    }

    [Fact]
    public async Task Expected_cancellation_is_not_logged_as_an_error()
    {
        var directory = NewDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var workflow = new ScreenTranslationWorkflow(
            new ThrowingProvider(new OperationCanceledException(cancellation.Token)),
            new PluginLog(directory));

        var outcome = await workflow.TranslateAsync(Guid.NewGuid(), Source(), "en", cancellation.Token);

        Assert.Equal(TranslationFailure.Canceled, outcome.Failure);
        Assert.False(File.Exists(Path.Combine(directory, "plugin.log")));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, TranslationFailure.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, TranslationFailure.Service)]
    public async Task Http_failures_keep_their_existing_categories(
        HttpStatusCode statusCode,
        TranslationFailure expected)
    {
        var workflow = new ScreenTranslationWorkflow(
            new ThrowingProvider(new HttpRequestException("sensitive response", null, statusCode)),
            new PluginLog(NewDirectory()));

        var outcome = await workflow.TranslateAsync(Guid.NewGuid(), Source(), "en", CancellationToken.None);

        Assert.Equal(expected, outcome.Failure);
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "translation-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static BitmapSource Source()
    {
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        source.Freeze();
        return source;
    }

    private sealed class ThrowingProvider(Exception exception) : IImageTranslationProvider
    {
        public Task<BitmapSource> TranslateAsync(
            BitmapSource source,
            string target,
            CancellationToken cancellation) => throw exception;
    }
}
