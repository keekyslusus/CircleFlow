namespace CircleToSearch.Trigger;

// While a new shortcut is recorded, pressing the current one must not open the overlay over the recording window.
public sealed class HotkeyPause
{
    private int _paused;

    public bool IsActive => Volatile.Read(ref _paused) > 0;

    public Switch CreateSwitch() => new(this);

    public Action Guard(Action pressed, PluginLog log) => () =>
    {
        if (IsActive) log.Info(nameof(HotkeyPause), "hotkey ignored while a shortcut is recorded");
        else pressed();
    };

    public sealed class Switch(HotkeyPause owner)
    {
        private bool _on;

        public void Set(bool on)
        {
            if (on == _on) return;
            _on = on;
            if (on) Interlocked.Increment(ref owner._paused);
            else Interlocked.Decrement(ref owner._paused);
        }
    }
}
