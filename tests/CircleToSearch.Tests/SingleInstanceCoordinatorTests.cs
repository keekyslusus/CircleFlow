using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void Only_the_owner_can_initialize_data_and_release_allows_another_owner()
    {
        var name = "Local\\CircleFlow.Tests." + Guid.NewGuid().ToString("N");
        using var first = SingleInstanceCoordinator.TryAcquire(name);
        Assert.NotNull(first);
        var acquired = true;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "secondary-" + Guid.NewGuid().ToString("N")));
        var second = new Thread(() =>
        {
            using var instance = SingleInstanceCoordinator.TryAcquire(name);
            acquired = instance is not null;
            if (acquired) AppDataDirectory.Initialize(paths);
        });
        second.Start();
        Assert.True(second.Join(TimeSpan.FromSeconds(5)));
        Assert.False(acquired);
        Assert.False(Directory.Exists(paths.RootDirectory));
        first.Dispose();
        using var replacement = SingleInstanceCoordinator.TryAcquire(name);
        Assert.NotNull(replacement);
    }

    [Fact]
    public void Session_identity_is_stable_and_contains_the_current_user()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        Assert.StartsWith("Local\\CircleFlow." + identity.User!.Value + ".", SingleInstanceCoordinator.CurrentSessionName);
        Assert.Equal(SingleInstanceCoordinator.CurrentSessionName, SingleInstanceCoordinator.CurrentSessionName);
    }
}
