using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class WebView2VisualSearchProviderTests
{
    [Fact]
    public async Task Ready_results_are_reported_as_handled()
    {
        var provider = ProviderReturning(WebView2SearchStatus.ResultsReady);

        var outcome = await provider.SearchAsync([1, 2, 3], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Null(outcome.ResultsUrl);
        Assert.Equal(UploadFailure.None, outcome.Failure);
    }

    [Theory]
    [InlineData(WebView2SearchStatus.RuntimeUnavailable, UploadFailure.BrowserRuntimeUnavailable)]
    [InlineData(WebView2SearchStatus.Failed, UploadFailure.BrowserAutomationFailed)]
    [InlineData(WebView2SearchStatus.Canceled, UploadFailure.Canceled)]
    public async Task Window_failure_is_mapped_to_provider_failure(
        WebView2SearchStatus status,
        UploadFailure expected)
    {
        var provider = ProviderReturning(status);

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(expected, outcome.Failure);
    }

    private static WebView2VisualSearchProvider ProviderReturning(WebView2SearchStatus status)
        => new((_, _) => Task.FromResult(status));
}
