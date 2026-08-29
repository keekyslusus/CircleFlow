using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CircleToSearch.Ui.Effects;

// A lightweight adorner keeps the effect separate from control templates and hit testing.
public sealed class ControlRippleHost : IDisposable
{
    private readonly Control _control;
    private RippleAdorner? _adorner;

    private ControlRippleHost(Control control)
    {
        _control = control;
        control.PreviewMouseLeftButtonDown += OnPointerDown;
        control.Unloaded += OnUnloaded;
    }

    public static ControlRippleHost Attach(Control control) => new(control);

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (!Capture.OverlayVisualFactory.AnimationsEnabled()) return;
        var layer = AdornerLayer.GetAdornerLayer(_control);
        if (layer is null) return;
        if (_adorner is not null) layer.Remove(_adorner);
        _adorner = new RippleAdorner(_control, e.GetPosition(_control));
        layer.Add(_adorner);
        _adorner.Begin(() =>
        {
            layer.Remove(_adorner);
            _adorner = null;
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

    public void Dispose()
    {
        _control.PreviewMouseLeftButtonDown -= OnPointerDown;
        _control.Unloaded -= OnUnloaded;
        if (_adorner is null) return;
        AdornerLayer.GetAdornerLayer(_control)?.Remove(_adorner);
        _adorner = null;
    }

    private sealed class RippleAdorner : Adorner
    {
        private readonly Ellipse _ellipse;
        private readonly VisualCollection _visuals;

        public RippleAdorner(UIElement adornedElement, Point origin) : base(adornedElement)
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            _ellipse = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Frozen(PluginPalette.ControlRipple),
                Opacity = 0.28,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Margin = new Thickness(origin.X - 6, origin.Y - 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            _visuals = new VisualCollection(this) { _ellipse };
        }

        public void Begin(Action completed)
        {
            var size = Math.Max(ActualWidth, ActualHeight) * 2.5 / 12;
            var scale = new DoubleAnimation(1, Math.Max(1, size), TimeSpan.FromMilliseconds(225))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            ((ScaleTransform)_ellipse.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            ((ScaleTransform)_ellipse.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
            var fade = new DoubleAnimation(0.28, 0, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(290),
            };
            fade.Completed += (_, _) => completed();
            _ellipse.BeginAnimation(OpacityProperty, fade);
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];
        protected override Size ArrangeOverride(Size finalSize)
        {
            Clip = new RectangleGeometry(new Rect(finalSize), Math.Min(22, finalSize.Height / 2), Math.Min(22, finalSize.Height / 2));
            _ellipse.Arrange(new Rect(finalSize));
            return finalSize;
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
