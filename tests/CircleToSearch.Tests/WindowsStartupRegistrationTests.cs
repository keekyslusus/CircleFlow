using Xunit;

namespace CircleToSearch.Tests;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public void Enabling_points_the_run_entry_at_this_executable_and_disabling_removes_it()
    {
        using var registry = new TestStartupRegistry(@"C:\Users\Test User\AppData\Local\Programs\CircleFlow\CircleFlow.exe");

        Assert.False(registry.Registration.IsEnabled);
        Assert.True(registry.Registration.TrySet(true));
        Assert.Equal("\"C:\\Users\\Test User\\AppData\\Local\\Programs\\CircleFlow\\CircleFlow.exe\" --autostart",
            registry.RunValue);
        Assert.True(registry.Registration.IsEnabled);

        Assert.True(registry.Registration.TrySet(false));
        Assert.Null(registry.RunValue);
        Assert.False(registry.Registration.IsEnabled);
        Assert.True(registry.Registration.TrySet(false));
    }

    [Fact]
    public void Task_manager_state_is_respected_and_cleared_when_the_user_changes_the_toggle()
    {
        using var registry = new TestStartupRegistry();
        registry.Registration.TrySet(true);

        registry.SetTaskManagerState(enabled: true);
        Assert.True(registry.Registration.IsEnabled);
        registry.SetTaskManagerState(enabled: false);
        Assert.False(registry.Registration.IsEnabled);
        Assert.NotNull(registry.RunValue);

        Assert.True(registry.Registration.TrySet(true));
        Assert.Null(registry.ApprovedValue);
        Assert.True(registry.Registration.IsEnabled);

        registry.SetTaskManagerState(enabled: false);
        Assert.True(registry.Registration.TrySet(false));
        Assert.Null(registry.ApprovedValue);
    }

    [Theory]
    [InlineData("\"D:\\Old\\CircleFlow\\CircleFlow.exe\" --autostart")]
    [InlineData("C:\\Apps\\CircleFlow\\CircleFlow.exe")]
    public void Entry_not_written_by_this_copy_is_off_until_this_copy_takes_it_over(string otherCommand)
    {
        using var registry = new TestStartupRegistry(@"C:\Apps\CircleFlow\CircleFlow.exe");
        registry.RunValue = otherCommand;

        Assert.False(registry.Registration.IsEnabled);
        Assert.True(registry.Registration.TrySet(true));
        Assert.Equal(registry.Registration.Command, registry.RunValue);
        Assert.True(registry.Registration.IsEnabled);
    }

    [Fact]
    public void Uninstall_removes_only_the_entry_of_this_copy()
    {
        using var registry = new TestStartupRegistry(@"C:\Apps\CircleFlow\current\CircleFlow.exe");
        const string other = "\"D:\\Portable\\CircleFlow.exe\" --autostart";
        registry.RunValue = other;
        registry.Registration.RemoveForUninstall();
        Assert.Equal(other, registry.RunValue);

        Assert.True(registry.Registration.TrySet(true));
        registry.SetTaskManagerState(enabled: false);
        registry.Registration.RemoveForUninstall();
        Assert.Null(registry.RunValue);
        Assert.Null(registry.ApprovedValue);
    }

    [Fact]
    public void Path_comparison_ignores_case_and_non_string_values_are_off()
    {
        using var registry = new TestStartupRegistry(@"C:\Apps\CircleFlow\CircleFlow.exe");
        registry.RunValue = "\"c:\\apps\\circleflow\\circleflow.EXE\" --autostart";
        Assert.True(registry.Registration.IsEnabled);

        registry.RunValue = 1;
        Assert.False(registry.Registration.IsEnabled);
    }

    [Fact]
    public void Write_failure_is_reported_and_keeps_the_previous_state()
    {
        using var registry = new TestStartupRegistry();

        using (registry.DenyWrites())
            Assert.False(registry.Registration.TrySet(true));
        Assert.Null(registry.RunValue);
        Assert.False(registry.Registration.IsEnabled);
    }
}
