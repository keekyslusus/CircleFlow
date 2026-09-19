using System.Collections.Concurrent;

namespace CircleToSearch;

internal sealed class ShutdownOperations(PluginLog log, string owner)
{
    private readonly ConcurrentQueue<Exception> _failures = new();

    public async Task RunAsync(string operation, Func<Task> action)
    {
        try { await Task.Run(action).ConfigureAwait(false); }
        catch (Exception exception)
        {
            IEnumerable<Exception> failures = exception is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : [exception];
            foreach (var failure in failures)
            {
                _failures.Enqueue(failure);
                log.SafeError(owner, operation, failure);
            }
        }
    }

    public Task DisposeAsync(string name, IDisposable? resource) => resource is null
        ? Task.CompletedTask
        : RunAsync($"dispose-{name}", () =>
        {
            resource.Dispose();
            return Task.CompletedTask;
        });

    public void ThrowIfFailed()
    {
        if (!_failures.IsEmpty) throw new AggregateException(_failures);
    }
}
