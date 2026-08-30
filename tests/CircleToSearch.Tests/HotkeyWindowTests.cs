using CircleToSearch.Interop;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class HotkeyWindowTests
{
    [Fact]
    public void Dispose_releases_dispatcher_when_message_window_was_not_created()
    {
        var dispatcher = new TestStaDispatcher { ExecuteSentAction = false };
        var window = new HotkeyWindow(dispatcher, NewLog());

        window.Dispose();
        window.Dispose();

        Assert.Equal(1, dispatcher.DisposeCalls);
    }

    [Fact]
    public void Composition_root_preserves_hotkey_thread_name()
    {
        Assert.Equal("CircleToSearch hotkey", CompositionRoot.HotkeyThreadName);
    }

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CircleToSearch.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }
}
