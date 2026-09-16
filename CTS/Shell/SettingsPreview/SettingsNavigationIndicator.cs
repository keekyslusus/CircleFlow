using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsNavigationIndicator : IDisposable
{
    private readonly Grid _host;
    private readonly StackPanel _items;
    private readonly Border _selection;
    private readonly TranslateTransform _translation;
    private readonly RoutedEventHandler _checkedHandler;
    private bool _positioned;

    internal SettingsNavigationIndicator(Grid host, StackPanel items, Border selection)
    {
        _host = host;
        _items = items;
        _selection = selection;
        _translation = (TranslateTransform)selection.RenderTransform;
        _checkedHandler = OnChecked;
        items.AddHandler(ToggleButton.CheckedEvent, _checkedHandler);
        items.Loaded += OnLoaded;
        items.Unloaded += OnUnloaded;
        items.SizeChanged += OnSizeChanged;
    }

    private void OnChecked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is RadioButton item && _items.Children.Contains(item)) MoveToSelection(animate: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => MoveToSelection(animate: false);
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => MoveToSelection(animate: false);
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _translation.BeginAnimation(TranslateTransform.YProperty, null);
        _positioned = false;
    }

    private void MoveToSelection(bool animate)
    {
        if (!_items.IsLoaded) return;
        var selected = _items.Children.OfType<RadioButton>().FirstOrDefault(item => item.IsChecked == true);
        if (selected is null) return;
        _items.UpdateLayout();
        var target = selected.TranslatePoint(new Point(), _host).Y;
        var current = _translation.Y;
        _selection.Height = selected.ActualHeight;
        _selection.Visibility = Visibility.Visible;
        _translation.BeginAnimation(TranslateTransform.YProperty, null);
        _translation.Y = target;
        if (animate && _positioned && SystemParameters.ClientAreaAnimation && Math.Abs(current - target) > 0.1)
        {
            // Retarget from the displayed position so rapid selection changes never jump back.
            _translation.Y = current;
            var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            animation.Completed += (_, _) =>
            {
                _translation.Y = target;
                _translation.BeginAnimation(TranslateTransform.YProperty, null);
            };
            _translation.BeginAnimation(TranslateTransform.YProperty, animation);
        }
        _positioned = true;
    }

    public void Dispose()
    {
        _translation.BeginAnimation(TranslateTransform.YProperty, null);
        _items.RemoveHandler(ToggleButton.CheckedEvent, _checkedHandler);
        _items.Loaded -= OnLoaded;
        _items.Unloaded -= OnUnloaded;
        _items.SizeChanged -= OnSizeChanged;
    }
}
