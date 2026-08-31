namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CircleToSearch.Search;
using CircleToSearch.Ui;

internal static class ProviderMenuVisualPresenter
{
    internal static void UpdateProvider(
        ProviderMenuVisual visual,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string selectedProviderId,
        UiStrings strings,
        bool lightTheme)
    {
        var selected = providers.First(provider =>
            string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase));
        visual.Content.Content = ProviderVisualCatalog.Create(selected, strings, lightTheme);
        visual.Button.ToolTip = strings.SelectSearchProvider(selected.DisplayName);
        AutomationProperties.SetName(visual.Button, strings.SelectSearchProvider(selected.DisplayName));
        ProviderMenuVisualFactory.PopulateMenuItems(visual, providers, selectedProviderId, lightTheme, strings);
    }

    internal static void SetOpen(ProviderMenuVisual visual, Grid coordinateRoot, bool open)
    {
        if (!open)
        {
            visual.Menu.Tag = false;
            AnimateChevron(visual.Chevron, 0);
            if (visual.Menu.Visibility != Visibility.Visible) return;
            if (!OverlayVisualResources.AnimationsEnabled())
            {
                visual.Menu.Visibility = Visibility.Collapsed;
                return;
            }
            var duration = TimeSpan.FromMilliseconds(150);
            var fade = OverlayVisualResources.Animate(1, 0, duration);
            fade.Completed += (_, _) =>
            {
                if (visual.Menu.Tag is false) visual.Menu.Visibility = Visibility.Collapsed;
            };
            visual.Menu.BeginAnimation(UIElement.OpacityProperty, fade);
            var transform = visual.Menu.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
            visual.Menu.RenderTransform = transform;
            transform.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                OverlayVisualResources.Animate(1, 0.95, duration));
            transform.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                OverlayVisualResources.Animate(1, 0.95, duration));
            return;
        }

        visual.Menu.Tag = true;
        AnimateChevron(visual.Chevron, 180);
        visual.Menu.Visibility = Visibility.Visible;
        if (visual.Button.IsLoaded)
        {
            visual.Menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var anchor = visual.Button.TranslatePoint(new Point(visual.Button.ActualWidth / 2, 0), coordinateRoot);
            var left = Math.Clamp(
                anchor.X - visual.Menu.DesiredSize.Width / 2,
                0,
                Math.Max(0, coordinateRoot.ActualWidth - visual.Menu.DesiredSize.Width));
            var top = Math.Clamp(
                anchor.Y - 10 - visual.Menu.DesiredSize.Height,
                0,
                Math.Max(0, coordinateRoot.ActualHeight - visual.Menu.DesiredSize.Height));
            Canvas.SetLeft(visual.Menu, left);
            Canvas.SetTop(visual.Menu, top);
        }
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            visual.Menu.Opacity = 1;
            return;
        }
        visual.Menu.Opacity = 0;
        visual.Menu.RenderTransform = new ScaleTransform(0.95, 0.95);
        visual.Menu.BeginAnimation(
            UIElement.OpacityProperty,
            OverlayVisualResources.Animate(0, 1, OverlayVisualResources.EntranceDuration));
        ((ScaleTransform)visual.Menu.RenderTransform).BeginAnimation(
            ScaleTransform.ScaleXProperty,
            OverlayVisualResources.Animate(0.95, 1, OverlayVisualResources.EntranceDuration));
        ((ScaleTransform)visual.Menu.RenderTransform).BeginAnimation(
            ScaleTransform.ScaleYProperty,
            OverlayVisualResources.Animate(0.95, 1, OverlayVisualResources.EntranceDuration));
    }

    private static void AnimateChevron(System.Windows.Shapes.Path chevron, double angle)
    {
        if (chevron.RenderTransform is not RotateTransform rotation) return;
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            rotation.Angle = angle;
            return;
        }
        rotation.BeginAnimation(
            RotateTransform.AngleProperty,
            OverlayVisualResources.Animate(rotation.Angle, angle, TimeSpan.FromMilliseconds(160)));
    }
}
