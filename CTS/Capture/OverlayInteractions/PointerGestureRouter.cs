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
        _visual.InputSurface.MouseLeftButtonUp += OnUp;
    }

    internal ActivePointerGesture ActiveGesture { get; private set; }

    internal void Cancel()
    {
        if (ActiveGesture == ActivePointerGesture.Lasso) _lasso.Cancel();
        if (ActiveGesture == ActivePointerGesture.Text) _text.CancelGesture();
        ActiveGesture = ActivePointerGesture.None;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visual.InputSurface.MouseLeftButtonDown -= OnDown;
        _visual.InputSurface.MouseMove -= OnMove;
        _visual.InputSurface.MouseLeftButtonUp -= OnUp;
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
        _text.Dismiss();
        if (!_modifiers().HasFlag(ModifierKeys.Alt) && _text.Begin(point))
            ActiveGesture = ActivePointerGesture.Text;
        else if (_lasso.Begin(point, e.OriginalSource))
            ActiveGesture = ActivePointerGesture.Lasso;
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_disposed || !_canAcceptInput()) return;
        var point = _pointerPosition(e);
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
}
