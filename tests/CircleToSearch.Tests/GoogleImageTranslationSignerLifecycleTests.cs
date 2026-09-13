using System.Net.Http;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleImageTranslationSignerLifecycleTests
{
    [Fact]
    public async Task Stop_cancels_a_queued_sign_and_then_stops_its_dispatcher()
    {
        using var http = new HttpClient();
        var dispatcher = new TestStaDispatcher();
        var signer = new GoogleImageTranslationSigner(
            http,
            dispatcher,
            () => throw new InvalidOperationException("A queued operation must not create a browser environment."));
        var sign = signer.SignAsync("request", "en", CancellationToken.None);
        Assert.False(sign.IsCompleted);
        dispatcher.TryPostResult = false;

        var stop = signer.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sign);
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, dispatcher.StopCalls);
        await signer.DisposeAsync();
    }
}
