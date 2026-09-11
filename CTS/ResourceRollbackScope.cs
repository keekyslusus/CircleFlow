namespace CircleToSearch;

internal sealed class ResourceRollbackScope(PluginLog log) : IDisposable
{
    private readonly List<IDisposable> _resources = [];
    private bool _committed;

    public T Own<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    public T Replace<T>(IDisposable child, T owner) where T : IDisposable
    {
        _resources.Remove(child);
        _resources.Add(owner);
        return owner;
    }

    public T TransferAllTo<T>(T owner) where T : IDisposable
    {
        _resources.Clear();
        _resources.Add(owner);
        return owner;
    }

    public void Commit()
    {
        _committed = true;
        _resources.Clear();
    }

    public void Dispose()
    {
        if (_committed) return;
        for (var index = _resources.Count - 1; index >= 0; index--)
        {
            try { _resources[index].Dispose(); }
            catch (Exception exception)
            {
                log.SafeError(nameof(ResourceRollbackScope), "rollback-resource", exception);
            }
        }
        _resources.Clear();
    }
}
