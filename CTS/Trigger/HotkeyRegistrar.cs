using CircleToSearch.Interop;
using CircleToSearch.Ui;

namespace CircleToSearch.Trigger;

public sealed record HotkeyRegistrationStatus(string Gesture, bool IsActive);
public sealed record HotkeyApplyResult(bool Success, HotkeyRegistrationStatus Status);

public sealed class HotkeyRegistrar
{
    private readonly Func<uint, uint, (bool Success, int Error)> _register;
    private readonly Func<bool> _unregister;
    private readonly UiStrings _strings;
    private readonly PluginLog _log;
    private readonly object _gate = new();
    private HotkeyRegistrationStatus _status = new(string.Empty, false);

    public HotkeyRegistrar(HotkeyWindow window, UiStrings strings, PluginLog log)
        : this((modifiers, key) =>
        {
            var success = window.TryRegister(modifiers, key, out var error);
            return (success, error);
        }, window.TryUnregister, strings, log) { }

    internal HotkeyRegistrar(Func<uint, uint, (bool Success, int Error)> register,
        Func<bool> unregister, UiStrings strings, PluginLog log)
    {
        _register = register;
        _unregister = unregister;
        _strings = strings;
        _log = log;
    }

    public HotkeyRegistrationStatus Status { get { lock (_gate) return _status; } }

    public HotkeyApplyResult TryApply(string? gestureText)
    {
        lock (_gate)
        {
            if (gestureText is null)
            {
                if (_status.IsActive && !Unregister()) return new(false, _status);
                _status = new(string.Empty, false);
                return new(true, _status);
            }
            if (!HotkeyGestureParser.TryParse(gestureText, out var modifiers, out var key))
                return new(false, _status);
            var gesture = HotkeyGestureParser.Format(modifiers, key);
            if (_status.IsActive && gesture == _status.Gesture) return new(true, _status);
            var previous = _status;
            if (previous.IsActive && !Unregister()) return new(false, _status);
            if (Register(modifiers, key))
            {
                _status = new(gesture, true);
                return new(true, _status);
            }
            _status = new(gesture, false);
            if (previous.IsActive)
            {
                HotkeyGestureParser.TryParse(previous.Gesture, out var oldModifiers, out var oldKey);
                _status = previous with { IsActive = Register(oldModifiers, oldKey) };
            }
            return new(false, _status);
        }
    }

    private bool Register(uint modifiers, uint key)
    {
        try
        {
            var result = _register(modifiers, key);
            if (!result.Success)
                _log.Warn(nameof(HotkeyRegistrar), $"RegisterHotKey failed (error {result.Error})");
            return result.Success;
        }
        catch (Exception exception)
        {
            _log.SafeError(nameof(HotkeyRegistrar), "register-hotkey", exception);
            return false;
        }
    }

    private bool Unregister()
    {
        try { return _unregister(); }
        catch (Exception exception)
        {
            _log.SafeError(nameof(HotkeyRegistrar), "unregister-hotkey", exception);
            return false;
        }
    }

    public string DescribeStatus()
    {
        var status = Status;
        return status.Gesture.Length == 0 ? _strings.HotkeyStatusNone
            : status.IsActive ? _strings.HotkeyStatusActive(status.Gesture)
            : _strings.HotkeyStatusUnavailable(status.Gesture);
    }
}
