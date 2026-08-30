using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class VisualSearchWorkflowTests
{
    [Fact]
    public async Task Successful_url_result_disposes_bitmap_transitions_once_and_opens_url()
    {
        using var harness = new Harness();
        harness.Provider.Outcome = VisualSearchOutcome.Ok("https://example.com/result");
        var bitmap = new Bitmap(2, 2);
        var transitions = 0;

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), bitmap),
            SearchProviderIds.GoogleLens,
            () => transitions++,
            CancellationToken.None);

        Assert.Equal(1, transitions);
        Assert.Equal(["https://example.com/result"], harness.Opened);
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
    }

    [Fact]
    public async Task Handled_result_does_not_open_an_external_url()
    {
        using var harness = new Harness();

        await harness.ExecuteAsync();

        Assert.Empty(harness.Opened);
        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Yandex_selection_routes_once_and_opens_one_result_url()
    {
        using var harness = new Harness();
        harness.Yandex.Outcome = VisualSearchOutcome.Ok("https://yandex.example/result");

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            SearchProviderIds.YandexImages,
            () => { },
            CancellationToken.None);

        Assert.Equal(0, harness.Provider.Calls);
        Assert.Equal(1, harness.Yandex.Calls);
        Assert.Equal(["https://yandex.example/result"], harness.Opened);
    }

    [Fact]
    public async Task Unknown_provider_routes_to_default()
    {
        using var harness = new Harness();

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            "missing",
            () => { },
            CancellationToken.None);

        Assert.Equal(1, harness.Provider.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Upload_callback_runs_once_before_provider_invocation()
    {
        using var harness = new Harness();
        var events = new List<string>();
        harness.Provider.OnCall = () => events.Add("provider");

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            SearchProviderIds.GoogleLens,
            () => events.Add("upload"),
            CancellationToken.None);

        Assert.Equal(["upload", "provider"], events);
    }

    [Fact]
    public async Task Crop_exception_disposes_bitmap_without_transitioning_or_notifying()
    {
        using var harness = new Harness(crop: (_, _) => throw new InvalidOperationException("crop failed"));
        var bitmap = new Bitmap(2, 2);
        var transitions = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), bitmap),
            SearchProviderIds.GoogleLens,
            () => transitions++,
            CancellationToken.None));

        Assert.Equal("crop failed", exception.Message);
        Assert.Equal(0, transitions);
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Provider_exception_propagates_after_bitmap_is_disposed()
    {
        using var harness = new Harness();
        harness.Provider.Exception = new InvalidOperationException("provider failed");
        var bitmap = new Bitmap(2, 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), bitmap),
            SearchProviderIds.GoogleLens,
            () => { },
            CancellationToken.None));

        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
        Assert.Empty(harness.Notifier.Errors);
    }

    [Theory]
    [InlineData(UploadFailure.UnexpectedStatus, "503")]
    [InlineData(UploadFailure.BadResponse, "unexpected response")]
    [InlineData(UploadFailure.PolicyRejection, "unexpected")]
    [InlineData(UploadFailure.Timeout, "timed out")]
    [InlineData(UploadFailure.NetworkError, "network error")]
    [InlineData(UploadFailure.BrowserRuntimeUnavailable, "Google Lens")]
    [InlineData(UploadFailure.BrowserAutomationFailed, "Google Lens")]
    public async Task Expected_failure_maps_to_its_existing_message(UploadFailure failure, string expected)
    {
        using var harness = new Harness();
        harness.Provider.Outcome = VisualSearchOutcome.Fail(failure, 503);

        await harness.ExecuteAsync();

        Assert.Contains(expected, Assert.Single(harness.Notifier.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Canceled_failure_does_not_notify()
    {
        using var harness = new Harness();
        harness.Provider.Outcome = VisualSearchOutcome.Fail(UploadFailure.Canceled);

        await harness.ExecuteAsync();

        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Url_open_failure_surfaces_the_existing_error()
    {
        using var harness = new Harness(openUrl: _ => false);
        harness.Provider.Outcome = VisualSearchOutcome.Ok("https://example.com/result");

        await harness.ExecuteAsync();

        Assert.Contains("open", Assert.Single(harness.Notifier.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Harness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;

        public Harness(
            Func<Bitmap, Rectangle, byte[]>? crop = null,
            Func<string, bool>? openUrl = null)
        {
            var log = NewLog();
            _router = new VisualSearchProviderRouter(
                [
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
                        () => Provider),
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"),
                        () => Yandex),
                ],
                SearchProviderIds.GoogleLens,
                log);
            Workflow = new VisualSearchWorkflow(
                _router,
                crop ?? ((_, _) => [1, 2, 3]),
                openUrl ?? (url => { Opened.Add(url); return true; }),
                Notifier,
                TestUiStrings.English,
                log);
        }

        public VisualSearchWorkflow Workflow { get; }
        public FakeProvider Provider { get; } = new();
        public FakeProvider Yandex { get; } = new();
        public TestPluginNotifier Notifier { get; } = new();
        public List<string> Opened { get; } = [];

        public Task ExecuteAsync() => Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            SearchProviderIds.GoogleLens,
            () => { },
            CancellationToken.None);

        public void Dispose() => _router.Dispose();
    }

    private sealed class FakeProvider : IVisualSearchProvider
    {
        public int Calls { get; private set; }
        public VisualSearchOutcome Outcome { get; set; } = VisualSearchOutcome.Handled();
        public Exception? Exception { get; set; }
        public Action? OnCall { get; set; }

        public Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            OnCall?.Invoke();
            return Exception is null
                ? Task.FromResult(Outcome)
                : Task.FromException<VisualSearchOutcome>(Exception);
        }
    }

    private static PluginLog NewLog()
    {
        var path = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new PluginLog(path);
    }
}
