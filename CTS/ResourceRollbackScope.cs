namespace CircleToSearch;

internal sealed class ResourceRollbackScope(PluginLog log) : IAsyncDisposable
{
    private readonly List<(object Resource, Func<Task> Cleanup)> _resources = [];
    private bool _committed;
    private Task? _cleanup;

    public T Own<T>(T resource, Func<Task>? cleanup = null) where T : class
    {
        if (cleanup is null && resource is not IDisposable && resource is not IAsyncDisposable)
            throw new ArgumentException("The resource must support disposal or provide a cleanup action.", nameof(resource));
        _resources.Add((resource, cleanup ?? (() => resource is IAsyncDisposable asynchronous
            ? asynchronous.DisposeAsync().AsTask()
            : DisposeResource((IDisposable)resource))));
        return resource;
    }

    public T Replace<T>(object child, T owner, Func<Task>? cleanup = null) where T : class
    {
        _resources.RemoveAll(entry => ReferenceEquals(entry.Resource, child));
        return Own(owner, cleanup);
    }

    public T TransferAllTo<T>(T owner) where T : class
    {
        _resources.Clear();
        return Own(owner);
    }

    public void Commit()
    {
        _committed = true;
        _resources.Clear();
    }

    public ValueTask DisposeAsync() => new(_cleanup ??= CleanupAsync());

    private static Task DisposeResource(IDisposable resource)
    {
        resource.Dispose();
        return Task.CompletedTask;
    }

    private async Task CleanupAsync()
    {
        if (_committed) return;
        var failures = new List<Exception>();
        for (var index = _resources.Count - 1; index >= 0; index--)
        {
            try { await Task.Run(_resources[index].Cleanup).ConfigureAwait(false); }
            catch (Exception exception)
            {
                failures.Add(exception);
                log.SafeError(nameof(ResourceRollbackScope), "rollback-resource", exception);
            }
        }
        _resources.Clear();
        if (failures.Count != 0) throw new AggregateException(failures);
    }
}
