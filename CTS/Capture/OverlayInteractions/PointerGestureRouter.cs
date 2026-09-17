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
    private readonly Action? _onRightClickCancel;
    private readonly Func<MouseEventArgs, Point> _pointerPosition;
    private readonly Func<ModifierKeys> _modifiers;
    private Point? _rightDownPoint;
    private bool _rightDragging;
    private bool _disposed;

    internal PointerGestureRouter(
        SelectionOverlayVisual visual,
        FrameworkElement coordinateRoot,
        SelectionOverlayController lasso,
        TextSelectionOverlayController text,
        Func<bool> canAcceptInput,
        Func<object?, Point, bool> canStart,
        Func<MouseEventArgs, Point>? pointerPosition = null,
        Func<ModifierKeys>? modifiers = null,
        Action? onRightClickCancel = null)
    {
        _visual = visual;
        _lasso = lasso;
        _text = text;
        _canAcceptInput = canAcceptInput;
        _canStart = canStart;
        _pointerPosition = pointerPosition ?? (e => e.GetPosition(coordinateRoot));
        _modifiers = modifiers ?? (() => Keyboard.Modifiers);
        _onRightClickCancel = onRightClickCancel;
        _visual.InputSurface.MouseLeftButtonDown += OnDown;
        _visual.InputSurface.MouseMove += OnMove;
        _visual.InputSurface.MouseLeftButtonUp += OnUp;
        _visual.InputSurface.MouseRightButtonDown += OnRightDown;
        _visual.InputSurface.MouseRightButtonUp += OnRightUp;
    }

    internal ActivePointerGesture ActiveGesture { get; private set; }

    internal void Cancel()
    {
        if (ActiveGesture == ActivePointerGesture.Lasso) _lasso.Cancel();
        if (ActiveGesture == ActivePointerGesture.Text) _text.CancelGesture();
        ActiveGesture = ActivePointerGesture.None;
        _rightDownPoint = null;
        _rightDragging = false;
        if (ReferenceEquals(Mouse.Captured, _visual.InputSurface))
            Mouse.Capture(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visual.InputSurface.MouseLeftButtonDown -= OnDown;
        _visual.InputSurface.MouseMove -= OnMove;
        _visual.InputSurface.MouseLeftButtonUp -= OnUp;
        _visual.InputSurface.MouseRightButtonDown -= OnRightDown;
        _visual.InputSurface.MouseRightButtonUp -= OnRightUp;
        Cancel();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_disposed || !_canAcceptInput()) return;
        var point = _pointerPosition(e);
        if (!_canStart(e.OriginalSource, point))
        {
            e.Handled = true;
            return;
        }

        if (_lasso.IsActionMenuOpen)
            _lasso.DismissActionMenu();
        _text.Dismiss();

        var isCtrl = _modifiers().HasFlag(ModifierKeys.Control);
        if (!_modifiers().HasFlag(ModifierKeys.Alt) && !isCtrl && _text.Begin(point))
            ActiveGesture = ActivePointerGesture.Text;
        else if (_lasso.Begin(point, e.OriginalSource, isActionMenu: isCtrl))
            ActiveGesture = ActivePointerGesture.Lasso;
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_disposed || !_canAcceptInput()) return;
        var point = _pointerPosition(e);

        if (_rightDownPoint is not null)
        {
            if (!_rightDragging)
            {
                var delta = point - _rightDownPoint.Value;
                if (delta.Length >= 4)
                {
                    _rightDragging = true;
                    _text.Dismiss();
                    _lasso.DismissActionMenu();
                    if (_lasso.Begin(_rightDownPoint.Value, e.OriginalSource, isActionMenu: true))
                    {
                        ActiveGesture = ActivePointerGesture.Lasso;
                        _lasso.Update(point);
                    }
                }
            }
            else if (ActiveGesture == ActivePointerGesture.Lasso)
            {
                _lasso.Update(point);
            }
            e.Handled = true;
            return;
        }

        if (ActiveGesture == ActivePointerGesture.Lasso) _lasso.Update(point);
        else if (ActiveGesture == ActivePointerGesture.Text) _text.Update(point);
        else _text.Hover(point);
        if (ActiveGesture != ActivePointerGesture.None) e.Handled = true;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_disposed || ActiveGesture == ActivePointerGesture.None) return;
        var point = _pointerPosition(e);
        if (ActiveGesture == ActivePointerGesture.Lasso) _lasso.Complete(point);
        else _text.Complete(point);
        ActiveGesture = ActivePointerGesture.None;
        e.Handled = true;
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        if (_disposed || !_canAcceptInput()) return;
        var point = _pointerPosition(e);
        if (!_canStart(e.OriginalSource, point))
        {
            e.Handled = true;
            return;
        }

        if (_lasso.IsActionMenuOpen || _text.IsActionMenuOpen)
        {
            _lasso.DismissActionMenu();
            _text.Dismiss();
            e.Handled = true;
            return;
        }

        _rightDownPoint = point;
        _rightDragging = false;
        _visual.InputSurface.CaptureMouse();
        e.Handled = true;
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        if (_disposed) return;
        if (_rightDownPoint is not null)
        {
            if (ReferenceEquals(Mouse.Captured, _visual.InputSurface))
                Mouse.Capture(null);

            var point = _pointerPosition(e);
            if (_rightDragging && ActiveGesture == ActivePointerGesture.Lasso)
            {
                _lasso.Complete(point);
                ActiveGesture = ActivePointerGesture.None;
            }
            else if (!_rightDragging)
            {
                _onRightClickCancel?.Invoke();
            }

            _rightDownPoint = null;
            _rightDragging = false;
            e.Handled = true;
        }
    }
}
