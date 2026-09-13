using System.Windows;

namespace CircleToSearch.Shell;

internal sealed class AppLifetime(Application application, Func<Task> stopRuntime)
{
    private Task? _exitTask;

    public int Run()
    {
        application.SessionEnding += OnSessionEnding;
        try { return application.Run(); }
        finally { application.SessionEnding -= OnSessionEnding; }
    }

    public Task RequestExitAsync()
    {
        application.Dispatcher.VerifyAccess();
        if (_exitTask is not null) return _exitTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exitTask = completion.Task;
        _ = ExitAsync(completion);
        return _exitTask;
    }

    private async Task ExitAsync(TaskCompletionSource completion)
    {
        var exitCode = 0;
        try { await stopRuntime(); }
        catch { exitCode = 1; }
        finally
        {
            application.Shutdown(exitCode);
            completion.TrySetResult();
        }
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs args)
    {
        args.Cancel = true;
        _ = RequestExitAsync();
    }
}
