using System.Windows;
using System.Windows.Input;

namespace CircleToSearch.Capture.OverlayInteractions;

internal enum ActivePointerGesture { None, Lasso, Text }

internal sealed class PointerGestureRouter : IDisposable
{
    private readonly SelectionOverlayVisual _visual;
    private readonly SelectionOverlayController _lasso;
    private readonly TextSelectionOverlayController _text;
    private readonly Func<bool> _canAcceptInput;
    private readonly Func<object?, Point, bool> _canStart;
    private readonly Func<MouseEventArgs, Point> _pointerPosition;
    private readonly Func<ModifierKeys> _modifiers;
    private bool _disposed;
    private bool _startingGesture;
    private Point _rightOrigin;
    internal bool IsActionSelection { get; private set; }

    internal PointerGestureRouter(
        SelectionOverlayVisual visual,
        FrameworkElement coordinateRoot,
        SelectionOverlayController lasso,
        TextSelectionOverlayController text,
        Func<bool> canAcceptInput,
        Func<object?, Point, bool> canStart,
        Func<MouseEventArgs, Point>? pointerPosition = null,
        Func<ModifierKeys>? modifiers = null)
    {
        _visual = visual;
        _lasso = lasso;
        _text = text;
        _canAcceptInput = canAcceptInput;
        _canStart = canStart;
        _pointerPosition = pointerPosition ?? (e => e.GetPosition(coordinateRoot));
        _modifiers = modifiers ?? (() => Keyboard.Modifiers);
        _visual.InputSurface.MouseLeftButtonDown += OnDown;
        _visual.InputSurface.MouseMove += OnMove;
        _visual.InputSurface.MouseLeave += OnLeave;
        _visual.InputSurface.MouseLeftButtonUp += OnUp;
        _visual.InputSurface.MouseRightButtonDown += OnRightDown;
        _visual.InputSurface.MouseRightButtonUp += OnRightUp;
    }

    internal ActivePointerGesture ActiveGesture { get; private set; }

    internal ActivePointerGesture Cancel()
    {
        var gesture = ActiveGesture;
        var wasActionSelection = IsActionSelection;
        ActiveGesture = ActivePointerGesture.None;
        IsActionSelection = false;
        if (gesture == ActivePointerGesture.Lasso) _lasso.Cancel();
        if (gesture == ActivePointerGesture.Text) _text.CancelGesture();
        if (wasActionSelection && ReferenceEquals(Mouse.Captured, _visual.InputSurface)) Mouse.Capture(null);
        return gesture;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visual.InputSurface.MouseLeftButtonDown -= OnDown;
        _visual.InputSurface.MouseMove -= OnMove;
        _visual.InputSurface.MouseLeave -= OnLeave;
        _visual.InputSurface.MouseLeftButtonUp -= OnUp;
        _visual.InputSurface.MouseRightButtonDown -= OnRightDown;
        _visual.InputSurface.MouseRightButtonUp -= OnRightUp;
        Cancel();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_disposed || !_canAcceptInput() || IsActionSelection || ActiveGesture != ActivePointerGesture.None) return;
        var point = _pointerPosition(e);
        if (!_canStart(e.OriginalSource, point))
        {
            e.Handled = true;
            return;
        }
        _text.Dismiss();
        if (!_modifiers().HasFlag(ModifierKeys.Alt) && _text.Begin(point))
            ActiveGesture = ActivePointerGesture.Text;
        else if (_lasso.Begin(point, e.OriginalSource))
            ActiveGesture = ActivePointerGesture.Lasso;
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_disposed || _startingGesture || !_canAcceptInput()) return;
        var point = _pointerPosition(e);
        if (IsActionSelection && ActiveGesture == ActivePointerGesture.None)
        {
            if ((point - _rightOrigin).Length < 4) return;
            // WPF synchronizes the pointer during CaptureMouse and can re-enter MouseMove.
            _startingGesture = true;
            try
            {
                if (!_lasso.Begin(_rightOrigin)) { Cancel(); return; }
                IsActionSelection = true;
                ActiveGesture = ActivePointerGesture.Lasso;
            }
            finally { _startingGesture = false; }
        }
        if (ActiveGesture == ActivePointerGesture.Lasso) _lasso.Update(point);
        else if (ActiveGesture == ActivePointerGesture.Text) _text.Update(point);
        else _text.Hover(point);
        if (ActiveGesture != ActivePointerGesture.None) e.Handled = true;
    }

    // The tray and toolbars sit above the input surface, so leaving onto them produces no further MouseMove.
    private void OnLeave(object sender, MouseEventArgs e)
    {
        if (_disposed || IsActionSelection || ActiveGesture != ActivePointerGesture.None) return;
        _text.EndHover();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_disposed || IsActionSelection || ActiveGesture == ActivePointerGesture.None) return;
        var point = _pointerPosition(e);
        if (ActiveGesture == ActivePointerGesture.Lasso) _lasso.Complete(point);
        else _text.Complete(point);
        ActiveGesture = ActivePointerGesture.None;
        e.Handled = true;
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_disposed || !_canAcceptInput() || ActiveGesture != ActivePointerGesture.None || IsActionSelection) return;
        var point = _pointerPosition(e);
        if (!_canStart(e.OriginalSource, point)) return;
        _rightOrigin = point;
        IsActionSelection = true;
        _visual.InputSurface.CaptureMouse();
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_disposed || !IsActionSelection) return;
        if (ActiveGesture == ActivePointerGesture.Lasso) _lasso.Complete(_pointerPosition(e));
        else _lasso.RejectClick();
        ActiveGesture = ActivePointerGesture.None;
        IsActionSelection = false;
        if (ReferenceEquals(Mouse.Captured, _visual.InputSurface)) Mouse.Capture(null);
    }
}
