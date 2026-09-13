using CircleToSearch.Trigger;

namespace CircleToSearch.Tests;

internal sealed class TestHotkeyRegistration
{
    public TestHotkeyRegistration()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "hotkey-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Registrar = new HotkeyRegistrar((modifiers, key) =>
        {
            var gesture = HotkeyGestureParser.Format(modifiers, key);
            Attempts.Add(gesture);
            if (Results.Count != 0 && !Results.Dequeue()) return (false, 1409);
            Current = gesture;
            return (true, 0);
        }, () =>
        {
            UnregisterCalls++;
            if (!UnregisterSucceeds) return false;
            Current = null;
            return true;
        }, TestUiStrings.English, new PluginLog(directory));
    }

    public HotkeyRegistrar Registrar { get; }
    public Queue<bool> Results { get; } = new();
    public List<string> Attempts { get; } = [];
    public string? Current { get; private set; }
    public int UnregisterCalls { get; private set; }
    public bool UnregisterSucceeds { get; set; } = true;
}
