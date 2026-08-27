using CircleToSearch.Interop;

namespace CircleToSearch.Trigger;

public sealed class HotkeyRegistrar
{
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly HotkeyWindow _window;
    private readonly PluginLog _log;

    public HotkeyRegistrar(HotkeyWindow window, PluginLog log)
    {
        _window = window;
        _log = log;
    }

    public bool IsActive { get; private set; }

    public string Gesture { get; private set; } = string.Empty;

    public bool TryApply(string gestureText)
    {
        try
        {
            if (!HotkeyGestureParser.TryParse(gestureText, out var modifiers, out var virtualKey))
            {
                _log.Warn(nameof(HotkeyRegistrar), $"cannot parse hotkey gesture '{gestureText}'");
                IsActive = false;
                Gesture = gestureText;
                return false;
            }

            _window.TryUnregister();
            if (!_window.TryRegister(modifiers, virtualKey, out var errorCode))
            {
                var reason = errorCode == ErrorHotkeyAlreadyRegistered
                    ? "the combination is registered by another program"
                    : $"RegisterHotKey failed (error {errorCode})";
                _log.Warn(nameof(HotkeyRegistrar), $"hotkey '{gestureText}' was not registered: {reason}");
                IsActive = false;
                Gesture = gestureText;
                return false;
            }

            _log.Info(nameof(HotkeyRegistrar), $"hotkey '{gestureText}' registered");
            IsActive = true;
            Gesture = gestureText;
            return true;
        }
        catch (Exception exception)
        {
            _log.Error(nameof(HotkeyRegistrar), "hotkey registration failed", exception);
            IsActive = false;
            Gesture = gestureText;
            return false;
        }
    }

    public string DescribeStatus()
        => Gesture.Length == 0
            ? "none"
            : IsActive
                ? $"{Gesture} active"
                : $"{Gesture} not registered (combination may be in use)";
}
