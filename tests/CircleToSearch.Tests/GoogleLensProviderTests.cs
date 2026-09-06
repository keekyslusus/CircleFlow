using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensProviderTests
{
    [Fact]
    public async Task Preparation_creates_a_fresh_browser_operation_without_external_fallback()
    {
        var operations = new List<FakeOperation>();
        var provider = new GoogleLensProvider(_ =>
        {
            var operation = new FakeOperation();
            operations.Add(operation);
            return operation;
        });

        var first = await provider.PrepareAsync([1, 2, 3], CancellationToken.None);
        var second = await provider.PrepareAsync([4, 5, 6], CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(2, operations.Count);
        Assert.Equal(PreparedVisualSearchKind.BrowserOperation, first.PreparedSearch!.Kind);
        Assert.Same(operations[0], first.PreparedSearch.RequireBrowserOperation());
        Assert.Same(operations[1], second.PreparedSearch!.RequireBrowserOperation());
        Assert.Null(first.PreparedSearch.ExternalFallbackUrl);
    }

    [Fact]
    public async Task Canceled_preparation_does_not_create_an_operation()
    {
        var calls = 0;
        var provider = new GoogleLensProvider(_ =>
        {
            calls++;
            return new FakeOperation();
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await provider.PrepareAsync([1], cancellation.Token);

        Assert.False(outcome.Success);
        Assert.Equal(UploadFailure.Canceled, outcome.Failure);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Provider_does_not_construct_the_browser_operation_dependency()
    {
        var source = File.ReadAllText(Path.Combine(
            TestOutputPaths.RepoDirectory,
            "CTS",
            "Search",
            "GoogleLensProvider.cs"));

        Assert.DoesNotContain("new GoogleLensBrowserOperation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PluginLog", source, StringComparison.Ordinal);
    }

    private sealed class FakeOperation : IVisualSearchBrowserOperation
    {
        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
            => Task.FromResult(VisualSearchBrowserOperationStatus.Succeeded);
    }
}
