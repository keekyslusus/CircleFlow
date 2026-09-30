using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CircleToSearch.Ui;

internal sealed class SmoothScrollMotionController : IDisposable
{
    private const double MaximumDisplacement = 64;
    private const double SpringFrequency = 12;
    private const double ScrollFrequency = 22;
    private readonly ScrollViewer _scroll;
    private readonly TranslateTransform _translation;
    private TimeSpan? _lastFrame;
    private double _velocity;
    private double _scrollPosition;
    private double _scrollTarget;
    private double _scrollVelocity;
    private double? _requestedOffset;
    private bool _scrolling;
    private int _wheelDirection;
    private bool _animating;
    private bool _disposed;

    internal SmoothScrollMotionController(ScrollViewer scroll, TranslateTransform translation)
    {
        _scroll = scroll;
        _translation = translation;
        scroll.PreviewMouseWheel += OnMouseWheel;
        scroll.PreviewMouseDown += OnMouseDown;
        scroll.PreviewKeyDown += OnKeyDown;
        scroll.SizeChanged += OnSizeChanged;
        scroll.Unloaded += OnUnloaded;
        scroll.ScrollChanged += OnScrollChanged;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_disposed || !SystemParameters.ClientAreaAnimation || SystemParameters.WheelScrollLines == 0 || _scroll.ScrollableHeight <= 1 ||
            e.Delta == 0 || e.Handled || HasOwnWheelBehavior(e.OriginalSource as DependencyObject)) return;

        var beyondTop = e.Delta > 0 && _scroll.VerticalOffset <= 0.5;
        var beyondBottom = e.Delta < 0 && _scroll.VerticalOffset >= _scroll.ScrollableHeight - 0.5;
        if (!beyondTop && !beyondBottom)
        {
            var direction = Math.Sign(e.Delta);
            if (!_scrolling || direction != _wheelDirection)
            {
                _scrollPosition = _scroll.VerticalOffset;
                _scrollTarget = _scrollPosition;
                _scrollVelocity = 0;
            }
            var step = SystemParameters.WheelScrollLines < 0
                ? _scroll.ViewportHeight * 2
                : SystemParameters.WheelScrollLines * 32.0;
            _scrollTarget = Math.Clamp(_scrollTarget - e.Delta / 120.0 * step, 0, _scroll.ScrollableHeight);
            _wheelDirection = direction;
            _scrolling = true;
            _translation.Y = 0;
            _velocity = 0;
        }
        else
        {
            _scrolling = false;
            var resistance = Math.Max(0, 1 - Math.Abs(_translation.Y) / MaximumDisplacement);
            _velocity = Math.Clamp(_velocity + e.Delta / 120.0 * 650 * resistance, -900, 900);
        }
        if (!_animating)
        {
            _animating = true;
            _lastFrame = null;
            CompositionTarget.Rendering += OnRendering;
        }
        e.Handled = true;
    }

    private bool HasOwnWheelBehavior(DependencyObject? source)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, _scroll);)
        {
            if (current is ComboBox or TextBoxBase or ScrollBar or ScrollViewer) return true;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || !_scroll.IsVisible)
        {
            Reset();
            return;
        }
        // Frame time comes from the WPF animation clock, keeping this motion in step with the other animations.
        var now = ((RenderingEventArgs)e).RenderingTime;
        var elapsed = _lastFrame is { } last ? (now - last).TotalSeconds : 0;
        _lastFrame = now;

        if (_scrolling)
        {
            var remaining = _scrollPosition - _scrollTarget;
            var scrollDecay = Math.Exp(-ScrollFrequency * elapsed);
            var scrollCombined = _scrollVelocity + ScrollFrequency * remaining;
            _scrollPosition = _scrollTarget + (remaining + scrollCombined * elapsed) * scrollDecay;
            _scrollVelocity = (_scrollVelocity - ScrollFrequency * scrollCombined * elapsed) * scrollDecay;
            if (Math.Abs(_scrollPosition - _scrollTarget) < 0.1 && Math.Abs(_scrollVelocity) < 1)
            {
                _scrollPosition = _scrollTarget;
                _scrolling = false;
            }
            // ScrollToVerticalOffset is queued by WPF; recognize its later ScrollChanged notification.
            _requestedOffset = Math.Clamp(_scrollPosition, 0, _scroll.ScrollableHeight);
            _scroll.ScrollToVerticalOffset(_requestedOffset.Value);
        }

        // The exact critically damped spring stays stable across frame rates and returns without wobbling.
        var position = _translation.Y;
        var decay = Math.Exp(-SpringFrequency * elapsed);
        var combined = _velocity + SpringFrequency * position;
        var next = (position + combined * elapsed) * decay;
        _velocity = (_velocity - SpringFrequency * combined * elapsed) * decay;
        _translation.Y = Math.Clamp(next, -MaximumDisplacement, MaximumDisplacement);
        if (Math.Abs(next) >= MaximumDisplacement) _velocity = 0;
        if (!_scrolling && Math.Abs(_translation.Y) < 0.1 && Math.Abs(_velocity) < 1) Reset();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e) => Reset();
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Space) Reset();
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Reset();
    private void OnUnloaded(object sender, RoutedEventArgs e) => Reset();
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _scroll)) return;
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0 ||
            (e.VerticalChange != 0 && (!_requestedOffset.HasValue ||
                Math.Abs(_scroll.VerticalOffset - _requestedOffset.Value) > 1))) Reset();
    }

    internal void Reset()
    {
        if (_animating) CompositionTarget.Rendering -= OnRendering;
        _animating = false;
        _lastFrame = null;
        _velocity = 0;
        _scrolling = false;
        _scrollVelocity = 0;
        _requestedOffset = null;
        _translation.Y = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
        _scroll.PreviewMouseWheel -= OnMouseWheel;
        _scroll.PreviewMouseDown -= OnMouseDown;
        _scroll.PreviewKeyDown -= OnKeyDown;
        _scroll.SizeChanged -= OnSizeChanged;
        _scroll.Unloaded -= OnUnloaded;
        _scroll.ScrollChanged -= OnScrollChanged;
    }
}
