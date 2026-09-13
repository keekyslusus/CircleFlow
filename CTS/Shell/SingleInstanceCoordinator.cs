using System.Diagnostics;
using System.Security.Principal;

namespace CircleToSearch.Shell;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;

    private SingleInstanceCoordinator(Mutex mutex) => _mutex = mutex;

    public static string CurrentSessionName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var process = Process.GetCurrentProcess();
            return $"Local\\CircleFlow.{identity.User!.Value}.{process.SessionId}";
        }
    }

    public static SingleInstanceCoordinator? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            try
            {
                if (mutex.WaitOne(0)) return new SingleInstanceCoordinator(mutex);
            }
            catch (AbandonedMutexException) { return new SingleInstanceCoordinator(mutex); }
        }
        catch { mutex.Dispose(); throw; }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("The instance mutex must be released by its owning thread.");
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _disposed = true;
    }
}
