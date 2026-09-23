using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xunit;

namespace CircleToSearch.Tests;

// WPF allows one Application per process, so such tests rerun themselves in a child process.
// The child calls the test method directly: starting vstest there would cost about a second per test.
internal static class IsolatedTestHost
{
    private const string Marker = "CIRCLEFLOW_ISOLATED_TEST";

    public static async Task<bool> RunAsync<T>([CallerMemberName] string method = "")
    {
        var testName = typeof(T).FullName + "." + method;
        if (Environment.GetEnvironmentVariable(Marker) == testName) return false;

        var (exitCode, output) = await RunChildAsync(testName, Marker, testName, TimeSpan.FromSeconds(30));
        Assert.True(exitCode == 0, output);
        return true;
    }

    // Runs one test method of this assembly in a child process; the variable tells that run it is the child.
    public static async Task<(int ExitCode, string Output)> RunChildAsync(
        string testName, string variable, string value, TimeSpan timeout)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(IsolatedTestHost).Assembly.Location);
        start.ArgumentList.Add(testName);
        start.Environment[variable] = value;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(timeout); }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("The child test process timed out: " + testName);
        }
        return (process.ExitCode, await output + await error);
    }

    private static int Main(string[] args)
    {
        try
        {
            var testName = args.Single();
            var separator = testName.LastIndexOf('.');
            var type = typeof(IsolatedTestHost).Assembly.GetType(testName[..separator], throwOnError: true)!;
            var method = type.GetMethod(testName[(separator + 1)..])
                ?? throw new MissingMethodException(type.FullName, testName[(separator + 1)..]);
            if (method.Invoke(Activator.CreateInstance(type), null) is Task task) task.GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception is System.Reflection.TargetInvocationException { InnerException: { } inner }
                ? inner
                : exception);
            return 1;
        }
    }
}
