using CircleToSearch.Interop;
using CircleToSearch.Trigger;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class HotkeyRegistrarTests
{
    [Fact]
    public void Invalid_text_and_unregister_failure_do_not_replace_the_previous_hotkey()
    {
        var native = new TestHotkeyRegistration();
        Assert.True(native.Registrar.TryApply("Ctrl+F7").Success);
        Assert.False(native.Registrar.TryApply("invalid").Success);
        Assert.Equal("Ctrl+F7", native.Current);
        native.UnregisterSucceeds = false;
        var result = native.Registrar.TryApply("Ctrl+F8");
        Assert.False(result.Success);
        Assert.True(result.Status.IsActive);
        Assert.Equal("Ctrl+F7", result.Status.Gesture);
        Assert.Single(native.Attempts);
    }

    [Fact]
    public void Native_conflict_restores_the_original_registration_and_dispose_releases_it()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "native-hotkey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = new PluginLog(directory);
        using var firstWindow = new HotkeyWindow(new StaDispatcher("CircleFlow hotkey rollback test 1"), log);
        using var secondWindow = new HotkeyWindow(new StaDispatcher("CircleFlow hotkey rollback test 2"), log);
        var first = new HotkeyRegistrar(firstWindow, TestUiStrings.English, log);
        var second = new HotkeyRegistrar(secondWindow, TestUiStrings.English, log);
        var candidates = Enumerable.Range(5, 8).Select(key => "Ctrl+Alt+Shift+F" + key).ToArray();
        var original = candidates.FirstOrDefault(gesture => first.TryApply(gesture).Success);
        Assert.NotNull(original);
        var occupied = candidates.FirstOrDefault(gesture => gesture != original && second.TryApply(gesture).Success);
        Assert.NotNull(occupied);
        var conflict = first.TryApply(occupied);
        Assert.False(conflict.Success);
        Assert.Equal(new HotkeyRegistrationStatus(original, true), conflict.Status);
        Assert.False(second.TryApply(original).Success);
        Assert.Equal(new HotkeyRegistrationStatus(occupied, true), second.Status);
        firstWindow.Dispose();
        Assert.True(second.TryApply(original).Success);
    }
}
