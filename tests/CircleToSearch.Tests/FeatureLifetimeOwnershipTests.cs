using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class FeatureLifetimeOwnershipTests
{
    [Fact]
    public async Task Failure_before_owner_creation_rolls_back_children_once()
    {
        var events = new List<string>();
        var outer = new ResourceRollbackScope(PluginRuntimeLifetimeTests.NewLog());
        var nested = outer.Own(new ResourceRollbackScope(PluginRuntimeLifetimeTests.NewLog()));
        nested.Own(new Resource("throttle", events));
        nested.Own(new Resource("http", events));
        await outer.DisposeAsync();
        await outer.DisposeAsync();
        Assert.Equal(["http", "throttle"], events);
    }

    [Fact]
    public async Task Failure_after_owner_creation_uses_owner_only_and_preserves_cleanup_errors()
    {
        var events = new List<string>();
        var error = new InvalidOperationException("throttle");
        var log = PluginRuntimeLifetimeTests.NewLog();
        var outer = new ResourceRollbackScope(log);
        var nested = outer.Own(new ResourceRollbackScope(log));
        var throttle = nested.Own(new Resource("throttle", events, error));
        var http = nested.Own(new Resource("http", events));
        nested.TransferAllTo(new MusicRecognitionLifetime(throttle, http, log));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => outer.DisposeAsync().AsTask());
        Assert.Contains(error, failure.Flatten().InnerExceptions);
        await Assert.ThrowsAsync<AggregateException>(() => outer.DisposeAsync().AsTask());
        Assert.Equal(["throttle", "http"], events);
    }

    [Fact]
    public async Task Signer_replacement_removes_the_dispatcher_cleanup_path()
    {
        var events = new List<string>();
        var log = PluginRuntimeLifetimeTests.NewLog();
        var outer = new ResourceRollbackScope(log);
        var nested = outer.Own(new ResourceRollbackScope(log));
        var http = nested.Own(new Resource("http", events));
        var dispatcher = nested.Own(new Resource("dispatcher", events));
        var signer = nested.Replace(dispatcher, new Resource("signer", events));
        nested.TransferAllTo(new ScreenTranslationLifetime(() =>
        {
            signer.Dispose();
            return Task.CompletedTask;
        }, http, null, log));
        await outer.DisposeAsync();
        Assert.Equal(["signer", "http"], events);
    }

    [Fact]
    public async Task Final_transfer_disarms_nested_scopes_and_integrated_stop_obeys_barriers()
    {
        var events = new List<string>();
        var log = PluginRuntimeLifetimeTests.NewLog();
        var outer = new ResourceRollbackScope(log);
        var session = PluginRuntimeLifetimeTests.Gate();
        var signer = PluginRuntimeLifetimeTests.Gate();
        var router = PluginRuntimeLifetimeTests.Gate();
        var signerStarted = PluginRuntimeLifetimeTests.Gate();
        var routerStarted = PluginRuntimeLifetimeTests.Gate();
        var musicScope = outer.Own(new ResourceRollbackScope(log));
        var music = musicScope.TransferAllTo(new MusicRecognitionLifetime(
            musicScope.Own(new Resource("throttle", events)),
            musicScope.Own(new Resource("music-http", events)), log));
        var translationScope = outer.Own(new ResourceRollbackScope(log));
        var translation = translationScope.TransferAllTo(new ScreenTranslationLifetime(
            () => { signerStarted.SetResult(); return signer.Task; },
            translationScope.Own(new Resource("translation-http", events)),
            translationScope.Own(new Resource("profiler", events)), log));
        var visualScope = outer.Own(new ResourceRollbackScope(log));
        var visual = visualScope.TransferAllTo(new VisualSearchLifetime(
            () => { routerStarted.SetResult(); return router.Task; },
            visualScope.Own(new Resource("trace-http", events)), log));
        var lifetime = new PluginRuntimeLifetime(() => session.Task,
            () => Task.CompletedTask, () => Task.CompletedTask,
            music.StopAsync, translation.StopAsync, visual.StopAsync, log);
        outer.TransferAllTo(new AsyncOwner(lifetime.StopAsync));

        var stopping = lifetime.StopAsync();
        await signerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        lock (events) Assert.Empty(events);
        session.SetResult();
        await routerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(SpinWait.SpinUntil(() => { lock (events) return events.Contains("music-http"); }, TimeSpan.FromSeconds(3)));
        lock (events)
        {
            Assert.DoesNotContain("translation-http", events);
            Assert.DoesNotContain("trace-http", events);
        }
        signer.SetResult();
        router.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(3));
        await outer.DisposeAsync();
        await outer.DisposeAsync();
        lock (events)
        {
            Assert.Equal(5, events.Count);
            Assert.Equal(1, events.Count(name => name == "throttle"));
            Assert.Equal(1, events.Count(name => name == "music-http"));
            Assert.Equal(1, events.Count(name => name == "translation-http"));
            Assert.Equal(1, events.Count(name => name == "profiler"));
            Assert.Equal(1, events.Count(name => name == "trace-http"));
            Assert.True(events.IndexOf("throttle") < events.IndexOf("music-http"));
            Assert.True(events.IndexOf("translation-http") < events.IndexOf("profiler"));
        }
    }

    private sealed class AsyncOwner(Func<Task> stop) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(stop());
    }
}
