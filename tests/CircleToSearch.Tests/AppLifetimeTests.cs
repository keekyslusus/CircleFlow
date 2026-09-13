using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Windows;
using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AppLifetimeTests
{
    [Fact]
    public void Host_is_a_versioned_windows_executable_with_an_sta_entry_point_and_no_Flow_reference()
    {
        var assembly = typeof(AppRuntime).Assembly;
        Assert.Equal("CircleFlow", assembly.GetName().Name);
        Assert.Equal(new Version(0, 5, 1, 0), assembly.GetName().Version);
        Assert.Equal("CircleFlow", assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.NotNull(assembly.EntryPoint!.GetCustomAttribute<STAThreadAttribute>());
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), name => name.Name!.StartsWith("Flow.Launcher"));
        using var stream = File.OpenRead(assembly.Location);
        using var reader = new PEReader(stream);
        Assert.Equal(Subsystem.WindowsGui, reader.PEHeaders.PEHeader!.Subsystem);
    }

    [Fact]
    public async Task Last_window_close_keeps_dispatching_and_explicit_exit_waits_for_cleanup_once()
    {
        // WPF cannot create another Application after shutdown, including on other test threads.
        const string isolatedHostVariable = "CIRCLEFLOW_LIFETIME_TEST_HOST";
        if (Environment.GetEnvironmentVariable(isolatedHostVariable) != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(AppLifetimeTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(AppLifetimeTests).FullName + "." +
                nameof(Last_window_close_keeps_dispatching_and_explicit_exit_waits_for_cleanup_once));
            start.Environment[isolatedHostVariable] = "1";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("The isolated WPF lifetime test timed out.");
            }
            Assert.True(process.ExitCode == 0, await output + await error);
            return;
        }

        Exception? failure = null;
        var exitCode = -1;
        var calls = 0;
        var cleanedUp = false;
        var thread = new Thread(() =>
        {
            var application = CompositionRoot.CreateApplication();
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifetime = new AppLifetime(application, async () =>
            {
                calls++;
                await cleanup.Task;
                cleanedUp = true;
            });
            application.Startup += async (_, _) =>
            {
                try
                {
                    Assert.Null(application.StartupUri);
                    Assert.Equal(ShutdownMode.OnExplicitShutdown, application.ShutdownMode);
                    var window = new Window { ShowActivated = false, ShowInTaskbar = false };
                    window.Show();
                    window.Close();
                    await application.Dispatcher.InvokeAsync(() => { });
                    Assert.False(application.Dispatcher.HasShutdownStarted);

                    var first = lifetime.RequestExitAsync();
                    Assert.Same(first, lifetime.RequestExitAsync());
                    Assert.False(first.IsCompleted);
                    Assert.False(application.Dispatcher.HasShutdownStarted);
                    await application.Dispatcher.InvokeAsync(() => cleanup.SetResult());
                    await first;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    cleanup.TrySetResult();
                    application.Shutdown(1);
                }
            };
            try { exitCode = lifetime.Run(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.Equal(0, exitCode);
        Assert.Equal(1, calls);
        Assert.True(cleanedUp);
    }
}
