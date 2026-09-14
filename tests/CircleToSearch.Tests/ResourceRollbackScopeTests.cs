using Xunit;

namespace CircleToSearch.Tests;

public sealed class ResourceRollbackScopeTests
{
    [Fact]
    public async Task Rollback_is_reverse_ordered_and_one_failure_does_not_skip_other_resources()
    {
        var events = new List<string>();
        var scope = new ResourceRollbackScope(NewLog());
        {
            scope.Own(new Resource("first", events));
            scope.Own(new Resource("second", events, throws: true));
            scope.Own(new Resource("third", events));
        }

        await Assert.ThrowsAsync<AggregateException>(() => scope.DisposeAsync().AsTask());
        Assert.Equal(["third", "second", "first"], events);
    }

    [Fact]
    public async Task Replacing_a_child_transfers_cleanup_to_its_owner()
    {
        var events = new List<string>();
        await using (var scope = new ResourceRollbackScope(NewLog()))
        {
            var child = scope.Own(new Resource("child", events));
            scope.Replace(child, new Resource("owner", events));
        }

        Assert.Equal(["owner"], events);
    }

    [Fact]
    public async Task Commit_disarms_rollback()
    {
        var events = new List<string>();
        await using (var scope = new ResourceRollbackScope(NewLog()))
        {
            scope.Own(new Resource("resource", events));
            scope.Commit();
        }

        Assert.Empty(events);
    }

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "rollback-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class Resource(string name, List<string> events, bool throws = false) : IDisposable
    {
        public void Dispose()
        {
            events.Add(name);
            if (throws) throw new InvalidOperationException("cleanup secret");
        }
    }
}
