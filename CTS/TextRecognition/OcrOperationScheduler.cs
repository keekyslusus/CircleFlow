namespace CircleToSearch.TextRecognition;

internal sealed class OcrOperationScheduler : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    internal OcrOperationScheduler(int maximumConcurrency)
    {
        if (maximumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _semaphore = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    internal async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
