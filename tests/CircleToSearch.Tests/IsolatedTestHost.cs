using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xunit;

namespace CircleToSearch.Tests;

internal static class IsolatedTestHost
{
    public static async Task<bool> RunAsync<T>([CallerMemberName] string method = "")
    {
        var testName = typeof(T).FullName + "." + method;
        const string marker = "CIRCLEFLOW_ISOLATED_TEST";
        if (Environment.GetEnvironmentVariable(marker) == testName) return false;

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(T).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + testName);
        start.Environment[marker] = testName;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("The isolated test timed out: " + testName);
        }
        Assert.True(process.ExitCode == 0, await output + await error);
        return true;
    }
}
