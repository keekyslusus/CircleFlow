using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensProviderTests
{
    [Fact]
    public async Task Ready_results_are_reported_as_handled()
    {
        var provider = ProviderReturning(GoogleLensSearchStatus.ResultsReady);

        var outcome = await provider.SearchAsync([1, 2, 3], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Null(outcome.ResultsUrl);
        Assert.Equal(UploadFailure.None, outcome.Failure);
    }

    [Theory]
    [InlineData(GoogleLensSearchStatus.RuntimeUnavailable, UploadFailure.BrowserRuntimeUnavailable)]
    [InlineData(GoogleLensSearchStatus.Failed, UploadFailure.BrowserAutomationFailed)]
    [InlineData(GoogleLensSearchStatus.Canceled, UploadFailure.Canceled)]
    public async Task Window_failure_is_mapped_to_provider_failure(
        GoogleLensSearchStatus status,
        UploadFailure expected)
    {
        var provider = ProviderReturning(status);

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(expected, outcome.Failure);
    }

    [Fact]
    public void Dispose_releases_the_owned_window_once()
    {
        var window = new TrackingDisposable();
        var provider = new GoogleLensProvider(
            (_, _) => Task.FromResult(GoogleLensSearchStatus.ResultsReady),
            window);

        provider.Dispose();
        provider.Dispose();

        Assert.Equal(1, window.DisposeCalls);
    }

    private static GoogleLensProvider ProviderReturning(GoogleLensSearchStatus status)
        => new((_, _) => Task.FromResult(status));

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }
}
