using Xunit;

namespace CircleToSearch.Tests;

// Preview renders and live browser/network tests run only on request; skipping them keeps
// an ordinary run from reporting them as passed when nothing was checked.
internal static class TestSwitches
{
    public static void Require(params string[] variables)
    {
        foreach (var variable in variables)
            Skip.IfNot(Environment.GetEnvironmentVariable(variable) == "1", $"Set {variable}=1 to run.");
    }
}
