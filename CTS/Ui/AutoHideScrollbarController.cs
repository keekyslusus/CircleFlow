using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CircleToSearch.Ui;

internal sealed class AutoHideScrollbarController : IDisposable
{
    private readonly ScrollViewer _scroll;
    private readonly ScrollBar _bar;
    private readonly Track _track;
    private readonly DispatcherTimer _hideTimer;
    private bool _dragging;
    private bool _disposed;
    private double _dragOffset;

    internal AutoHideScrollbarController(ScrollViewer scroll)
    {
        _scroll = scroll;
        scroll.ApplyTemplate();
        _bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
        _bar.ApplyTemplate();
        _track = (Track)_bar.Template.FindName("PART_Track", _bar);
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background, scroll.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(OverlayScrollbarPolicy.HideDelayMilliseconds),
        };
        _hideTimer.Tick += OnHideTimer;
        scroll.ScrollChanged += OnScrollChanged;
        scroll.Unloaded += OnUnloaded;
        _bar.PreviewMouseLeftButtonDown += OnPointerDown;
        _bar.PreviewMouseMove += OnPointerMove;
        _bar.PreviewMouseLeftButtonUp += OnPointerUp;
        _bar.LostMouseCapture += OnLostCapture;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _scroll)) return;
        if (_scroll.ScrollableHeight <= 1)
        {
            Reset();
            return;
        }
        if (e.VerticalChange == 0) return;
        _bar.IsHitTestVisible = true;
        FadeTo(1, OverlayScrollbarPolicy.FadeInMilliseconds);
        ScheduleHide();
    }

    private void FadeTo(double opacity, int milliseconds)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _bar.BeginAnimation(UIElement.OpacityProperty, null);
            _bar.Opacity = opacity;
            return;
        }
        _bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity,
            TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void ScheduleHide()
    {
        _hideTimer.Stop();
        if (!_dragging && !_disposed) _hideTimer.Start();
    }

    private void OnHideTimer(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        if (_dragging) return;
        _bar.IsHitTestVisible = false;
        FadeTo(0, OverlayScrollbarPolicy.FadeOutMilliseconds);
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (_scroll.ScrollableHeight <= 1) return;
        var y = e.GetPosition(_track).Y;
        var thumbHeight = _track.Thumb.ActualHeight;
        var top = _scroll.VerticalOffset / _scroll.ScrollableHeight * Math.Max(0, _track.ActualHeight - thumbHeight);
        _dragOffset = y >= top && y <= top + thumbHeight ? y - top : thumbHeight / 2;
        _dragging = _bar.CaptureMouse();
        if (!_dragging) return;
        _hideTimer.Stop();
        ScrollFromPointer(y);
        e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        ScrollFromPointer(e.GetPosition(_track).Y);
        e.Handled = true;
    }

    private void ScrollFromPointer(double y)
    {
        var range = _track.ActualHeight - _track.Thumb.ActualHeight;
        if (range <= 0) return;
        _scroll.ScrollToVerticalOffset(Math.Clamp((y - _dragOffset) / range, 0, 1) * _scroll.ScrollableHeight);
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _bar.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        _dragging = false;
        ScheduleHide();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Reset();

    private void Reset()
    {
        if (_bar.IsMouseCaptured) _bar.ReleaseMouseCapture();
        _hideTimer.Stop();
        _bar.IsHitTestVisible = false;
        _bar.BeginAnimation(UIElement.OpacityProperty, null);
        _bar.Opacity = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
        _hideTimer.Tick -= OnHideTimer;
        _scroll.ScrollChanged -= OnScrollChanged;
        _scroll.Unloaded -= OnUnloaded;
        _bar.PreviewMouseLeftButtonDown -= OnPointerDown;
        _bar.PreviewMouseMove -= OnPointerMove;
        _bar.PreviewMouseLeftButtonUp -= OnPointerUp;
        _bar.LostMouseCapture -= OnLostCapture;
    }
}
