using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class PreparedVisualSearchTests
{
    [Fact]
    public void Url_factory_exposes_only_the_url_variant()
    {
        var results = new Uri("https://example.com/results");
        var fallback = new Uri("https://example.com/fallback");

        var prepared = PreparedVisualSearch.ForUrl(results, fallback);

        Assert.Equal(PreparedVisualSearchKind.Url, prepared.Kind);
        Assert.Equal(results, prepared.RequireResultsUrl());
        Assert.Equal(fallback, prepared.ExternalFallbackUrl);
        Assert.Throws<InvalidOperationException>(prepared.RequireBrowserOperation);
    }

    [Fact]
    public void Operation_factory_exposes_only_the_operation_variant()
    {
        var operation = new FakeOperation();

        var prepared = PreparedVisualSearch.ForBrowserOperation(operation, null);

        Assert.Equal(PreparedVisualSearchKind.BrowserOperation, prepared.Kind);
        Assert.Same(operation, prepared.RequireBrowserOperation());
        Assert.Null(prepared.ExternalFallbackUrl);
        Assert.Throws<InvalidOperationException>(prepared.RequireResultsUrl);
    }

    [Fact]
    public void Factories_reject_relative_navigation_and_fallback_urls()
    {
        Assert.Throws<ArgumentException>(() =>
            PreparedVisualSearch.ForUrl(new Uri("relative", UriKind.Relative), null));
        Assert.Throws<ArgumentException>(() =>
            PreparedVisualSearch.ForUrl(
                new Uri("https://example.com"),
                new Uri("relative", UriKind.Relative)));
        Assert.Throws<ArgumentException>(() =>
            PreparedVisualSearch.ForBrowserOperation(
                new FakeOperation(),
                new Uri("relative", UriKind.Relative)));
    }

    [Fact]
    public void Failure_cannot_be_constructed_with_none()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            VisualSearchPreparationOutcome.Fail(UploadFailure.None));

    private sealed class FakeOperation : IVisualSearchBrowserOperation
    {
        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
            => Task.FromResult(VisualSearchBrowserOperationStatus.Succeeded);
    }
}
