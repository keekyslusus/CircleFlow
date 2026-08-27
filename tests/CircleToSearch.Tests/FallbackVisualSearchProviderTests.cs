using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class FallbackVisualSearchProviderTests
{
    [Fact]
    public async Task Primary_success_skips_the_fallback()
    {
        var primary = new FakeProvider(VisualSearchOutcome.Ok("https://lens.google.com/result"));
        var fallback = new FakeProvider(VisualSearchOutcome.Handled());
        var provider = new FallbackVisualSearchProvider(primary, fallback);

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Primary_failure_falls_back()
    {
        var primary = new FakeProvider(VisualSearchOutcome.Fail(LensUploadFailure.NetworkError));
        var fallback = new FakeProvider(VisualSearchOutcome.Handled());
        var provider = new FallbackVisualSearchProvider(primary, fallback);

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task User_cancellation_does_not_fall_back()
    {
        var primary = new FakeProvider(VisualSearchOutcome.Fail(LensUploadFailure.Canceled));
        var fallback = new FakeProvider(VisualSearchOutcome.Handled());
        var provider = new FallbackVisualSearchProvider(primary, fallback);

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(0, fallback.Calls);
    }

    private sealed class FakeProvider(VisualSearchOutcome outcome) : IVisualSearchProvider
    {
        public int Calls { get; private set; }

        public Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(outcome);
        }
    }
}
