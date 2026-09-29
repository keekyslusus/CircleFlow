using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class MusicRecognitionLifetimeTests
{
    [Fact]
    public async Task Disposes_in_order_once_even_when_throttle_fails()
    {
        var events = new List<string>();
        var failure = new InvalidOperationException("throttle");
        var lifetime = new MusicRecognitionLifetime(
            new Resource("throttle", events, failure), new Resource("http", events), PluginRuntimeLifetimeTests.NewLog());
        var first = lifetime.StopAsync();
        Assert.Same(first, lifetime.StopAsync());
        var error = await Assert.ThrowsAsync<AggregateException>(() => first.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Same(failure, Assert.Single(error.InnerExceptions));
        await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().AsTask());
        Assert.Equal(["throttle", "http"], events);
    }
}

public sealed class ScreenTranslationLifetimeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Waits_for_both_session_and_signer_before_disposal(bool sessionFirst)
    {
        var session = PluginRuntimeLifetimeTests.Gate();
        var signer = PluginRuntimeLifetimeTests.Gate();
        var signerStarted = PluginRuntimeLifetimeTests.Gate();
        var events = new List<string>();
        var lifetime = new ScreenTranslationLifetime(
            () => { signerStarted.SetResult(); return signer.Task; },
            new Resource("http", events), new Resource("profiler", events), PluginRuntimeLifetimeTests.NewLog());
        var first = lifetime.StopAsync(session.Task);
        await signerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Same(first, lifetime.StopAsync(Task.CompletedTask));
        Assert.False(lifetime.DisposeAsync().AsTask().IsCompleted);
        if (sessionFirst) session.SetResult();
        else signer.SetResult();
        Assert.Empty(events);
        Assert.False(first.IsCompleted);
        if (sessionFirst) signer.SetResult();
        else session.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["http", "profiler"], events);
    }

    [Fact]
    public async Task Errors_do_not_skip_resources_and_each_error_is_preserved()
    {
        var sessionError = new InvalidOperationException("session");
        var signerError = new InvalidOperationException("signer");
        var httpError = new InvalidOperationException("http");
        var profilerError = new InvalidOperationException("profiler");
        var events = new List<string>();
        var lifetime = new ScreenTranslationLifetime(
            () => Task.FromException(signerError),
            new Resource("http", events, httpError),
            new Resource("profiler", events, profilerError), PluginRuntimeLifetimeTests.NewLog());
        var error = await Assert.ThrowsAsync<AggregateException>(() => lifetime.StopAsync(Task.FromException(sessionError)));
        Assert.Equal(["http", "profiler"], events);
        Assert.Equal(4, error.InnerExceptions.Count);
        Assert.Contains(sessionError, error.InnerExceptions);
        Assert.Contains(signerError, error.InnerExceptions);
        Assert.Contains(httpError, error.InnerExceptions);
        Assert.Contains(profilerError, error.InnerExceptions);
    }

    [Fact]
    public async Task Null_profiler_and_rollback_disposal_are_supported()
    {
        var events = new List<string>();
        var lifetime = new ScreenTranslationLifetime(() => Task.CompletedTask,
            new Resource("http", events), null, PluginRuntimeLifetimeTests.NewLog());
        await lifetime.DisposeAsync();
        Assert.Equal(["http"], events);
    }
}

public sealed class VisualSearchLifetimeTests
{
    [Fact]
    public async Task Trace_http_waits_for_router_and_is_disposed_once()
    {
        var router = PluginRuntimeLifetimeTests.Gate();
        var started = PluginRuntimeLifetimeTests.Gate();
        var events = new List<string>();
        var lifetime = new VisualSearchLifetime(
            () => { started.SetResult(); return router.Task; },
            new Resource("http", events), PluginRuntimeLifetimeTests.NewLog());
        var first = lifetime.StopAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Same(first, lifetime.StopAsync());
        Assert.Empty(events);
        router.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        await lifetime.DisposeAsync();
        Assert.Equal(["http"], events);
    }

    [Fact]
    public async Task Router_and_http_failures_are_both_preserved()
    {
        var routerError = new InvalidOperationException("router");
        var httpError = new InvalidOperationException("http");
        var events = new List<string>();
        var lifetime = new VisualSearchLifetime(() => Task.FromException(routerError),
            new Resource("http", events, httpError), PluginRuntimeLifetimeTests.NewLog());
        var error = await Assert.ThrowsAsync<AggregateException>(() => lifetime.StopAsync());
        Assert.Equal(["http"], events);
        Assert.Contains(routerError, error.InnerExceptions);
        Assert.Contains(httpError, error.InnerExceptions);
    }
}

internal sealed class Resource(string name, List<string> events, Exception? failure = null) : IDisposable
{
    public void Dispose()
    {
        lock (events) events.Add(name);
        if (failure is not null) throw failure;
    }
}
