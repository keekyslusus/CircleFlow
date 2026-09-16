using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Shell.SettingsPreview;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SettingsPreviewTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dialog_and_scrim_animate_in_and_out_retarget_and_restore_focus_after_closing(bool light) => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, light).Window;
        try
        {
            window.Show();
            window.Activate();
            Pump();
            var edit = LogicalChildren(window).OfType<Button>().Single(button => Equals(button.Tag, "edit"));
            var workspace = Find<Grid>(window, "Workspace");
            var layer = Find<Border>(window, "DialogLayer");
            var surface = Find<FrameworkElement>(window, "DialogMotionSurface");
            var scrim = Find<Border>(window, "DialogScrim");
            var transforms = (TransformGroup)surface.RenderTransform;
            var scale = transforms.Children.OfType<ScaleTransform>().Single();
            var translation = transforms.Children.OfType<TranslateTransform>().Single();
            edit.Focus();
            Click(window, "edit");
            Assert.False(workspace.IsEnabled);
            Assert.Equal(Visibility.Visible, layer.Visibility);
            if (UiAnimationPolicy.Enabled)
            {
                PumpFor(60);
                Assert.InRange(surface.Opacity, 0.001, 0.999);
                Assert.InRange(scrim.Opacity, 0.001, 0.999);
                Assert.InRange(scale.ScaleX, 0.96, 0.9999);
                Assert.InRange(translation.Y, 0.001, 12);
                Assert.True(scrim.RenderTransform.Value.IsIdentity);
                var opacity = surface.Opacity;
                var backdrop = scrim.Opacity;
                var y = translation.Y;
                Click(window, "cancel");
                Assert.Equal(opacity, surface.Opacity);
                Assert.Equal(backdrop, scrim.Opacity);
                Assert.Equal(y, translation.Y);
                Assert.False(surface.IsHitTestVisible);
                Assert.False(workspace.IsEnabled);
                PumpFor(40);
                opacity = surface.Opacity;
                backdrop = scrim.Opacity;
                y = translation.Y;
                Click(window, "edit");
                Assert.Equal(opacity, surface.Opacity);
                Assert.Equal(backdrop, scrim.Opacity);
                Assert.Equal(y, translation.Y);
            }
            CompleteDialogTransition(window, open: true);
            Assert.True(surface.IsHitTestVisible);
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(1, scale.ScaleY);
            Assert.Equal(0, translation.Y);
            Assert.Equal(1, scrim.Opacity);
            Assert.False(workspace.IsEnabled);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-hotkey-dialog-open.png");

            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            });
            if (UiAnimationPolicy.Enabled)
            {
                PumpFor(65);
                Assert.Equal(Visibility.Visible, layer.Visibility);
                Assert.InRange(surface.Opacity, 0.001, 0.999);
                Assert.InRange(scrim.Opacity, 0.001, 0.999);
                Assert.False(workspace.IsEnabled);
                Assert.False(surface.IsHitTestVisible);
                if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                    Capture(window, $"settings-{(light ? "light" : "dark")}-hotkey-dialog-exit.png");
            }
            CompleteDialogTransition(window, open: false);
            Assert.True(workspace.IsEnabled);
            Assert.Equal(0, scrim.Opacity);
            Assert.Same(edit, Keyboard.FocusedElement);
            Click(window, "edit");
            window.Hide();
            PumpFor(240);
            window.Show();
            Pump();
            Assert.Equal(Visibility.Collapsed, layer.Visibility);
            Assert.True(workspace.IsEnabled);
            Click(window, "edit");
            window.Close();
            PumpFor(240);
            Assert.Equal(Visibility.Collapsed, layer.Visibility);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dropdowns_reveal_from_the_anchor_rotate_the_arrow_and_reset_on_close(bool light) => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, light).Window;
        try
        {
            Find<RadioButton>(window, "Nav_search").IsChecked = true;
            window.Show();
            window.Top = 20;
            Pump();
            var combo = Find<ComboBox>(window, "Provider");
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            var surface = (FrameworkElement)combo.Template.FindName("DropdownSurface", combo);
            var arrow = (FrameworkElement)combo.Template.FindName("DropdownArrow", combo);
            var rotation = (RotateTransform)arrow.RenderTransform;
            var reveal = (RectangleGeometry)surface.Clip;
            combo.Focus();
            combo.IsDropDownOpen = true;
            Pump();
            Assert.True(popup.IsOpen);
            Assert.Equal(PopupAnimation.None, popup.PopupAnimation);
            Assert.Equal(1, surface.Opacity);
            Assert.Equal(0, reveal.Rect.Y);
            if (UiAnimationPolicy.Enabled)
            {
                PumpFor(45);
                Assert.InRange(reveal.Rect.Height, 0.001, surface.ActualHeight - 0.001);
                Assert.InRange(rotation.Angle, 0.001, 179.999);
            }
            PumpFor(240);
            Assert.Equal(new Rect(surface.RenderSize), reveal.Rect);
            Assert.Equal(180, rotation.Angle);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
            {
                Capture(window, $"settings-{(light ? "light" : "dark")}-dropdown-arrow.png");
                CaptureVisual(surface, $"settings-{(light ? "light" : "dark")}-dropdown-menu.png");
            }
            combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(combo), 0, Key.Escape)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            });
            Pump();
            Assert.False(combo.IsDropDownOpen);
            Assert.True(reveal.Rect.IsEmpty);
            if (UiAnimationPolicy.Enabled)
            {
                PumpFor(45);
                Assert.InRange(rotation.Angle, 0.001, 179.999);
            }
            var angle = rotation.Angle;
            combo.IsDropDownOpen = true;
            Assert.Equal(angle, rotation.Angle);
            PumpFor(240);
            Assert.Equal(180, rotation.Angle);
            combo.IsDropDownOpen = false;
            PumpFor(200);
            Assert.Equal(0, rotation.Angle);

            popup.Placement = PlacementMode.Top;
            window.Top = 350;
            combo.IsDropDownOpen = true;
            Pump();
            if (UiAnimationPolicy.Enabled)
            {
                PumpFor(45);
                Assert.True(reveal.Rect.Y > 0);
                Assert.Equal(surface.ActualHeight, reveal.Rect.Bottom, 3);
            }
            window.Hide();
            Pump();
            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(0, rotation.Angle);
            Assert.True(reveal.Rect.IsEmpty);
            window.Show();
            window.Top = 20;
            Pump();
            foreach (var (name, page) in new[] { ("AppLanguage", "general"), ("Cleanup", "general"), ("OcrLanguage", "text"), ("TargetLanguage", "text") })
            {
                Find<RadioButton>(window, "Nav_" + page).IsChecked = true;
                CompletePageTransition(window);
                var other = Find<ComboBox>(window, name);
                other.BringIntoView();
                Pump();
                other.IsDropDownOpen = true;
                PumpFor(250);
                var otherSurface = (FrameworkElement)other.Template.FindName("DropdownSurface", other);
                Assert.Equal(new Rect(otherSurface.RenderSize), ((RectangleGeometry)otherSurface.Clip).Rect);
                other.IsDropDownOpen = false;
            }
            Find<RadioButton>(window, "Nav_search").IsChecked = true;
            CompletePageTransition(window);
            combo.IsDropDownOpen = true;
            window.Close();
            PumpFor(250);
            Assert.False(popup.IsOpen);
            Assert.Equal(0, rotation.Angle);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Toggles_have_solid_capsules_and_animate_without_jumping_on_reversal(bool light) => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, light).Window;
        try
        {
            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            window.Show();
            Pump();
            var toggle = Find<CheckBox>(window, "Launch");
            T Part<T>(string name) => (T)toggle.Template.FindName(name, toggle);
            var translation = (TranslateTransform)Part<Grid>("Thumb").RenderTransform;
            var onTrack = Part<System.Windows.Shapes.Rectangle>("OnTrack");
            var offTrack = Part<System.Windows.Shapes.Rectangle>("OffTrack");
            Assert.Equal(40, toggle.ActualWidth);
            Assert.Equal(20, toggle.ActualHeight);
            Assert.Equal(20, translation.X);
            Assert.Equal(1, onTrack.Opacity);
            Assert.Equal(0, offTrack.Opacity);
            var accent = ((SolidColorBrush)onTrack.Fill).Color;
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                var width = (int)(40 * scale);
                var height = (int)(20 * scale);
                var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(toggle);
                var pixels = new byte[width * height * 4];
                bitmap.CopyPixels(pixels, width * 4, 0);
                // The middle of the capsule has no thumb: its fill must remain solid from top to bottom.
                for (var y = 1; y < height - 1; y++)
                {
                    var index = (y * width + width / 2) * 4;
                    Assert.Equal(new[] { accent.B, accent.G, accent.R, accent.A }, pixels[index..(index + 4)]);
                }
            }

            toggle.IsChecked = false;
            if (UiAnimationPolicy.Enabled)
            {
                PumpFor(60);
                Assert.InRange(translation.X, 0.001, 19.999);
                Assert.InRange(onTrack.Opacity, 0.001, 0.999);
                Assert.Equal(1, onTrack.Opacity + offTrack.Opacity, 3);
                var position = translation.X;
                var opacity = onTrack.Opacity;
                toggle.IsChecked = true;
                Assert.Equal(position, translation.X);
                Assert.Equal(opacity, onTrack.Opacity);
                PumpFor(240);
                Assert.Equal(20, translation.X);
                Assert.Equal(1, onTrack.Opacity);
                toggle.IsChecked = false;
            }
            PumpFor(240);
            Assert.Equal(0, translation.X);
            Assert.Equal(0, onTrack.Opacity);
            Assert.Equal(1, offTrack.Opacity);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-toggle-off.png");

            // A disabled animation policy keeps the same endpoints, without delaying a toggle.
            var root = (FrameworkElement)VisualTreeHelper.GetChild(toggle, 0);
            var states = VisualStateManager.GetVisualStateGroups(root).OfType<VisualStateGroup>().Single();
            Assert.Equal(UiAnimationPolicy.ToggleTransitionDuration, states.Transitions.OfType<VisualTransition>().Single().GeneratedDuration);
            states.Transitions.OfType<VisualTransition>().Single().GeneratedDuration = new Duration(TimeSpan.Zero);
            toggle.IsChecked = true;
            Pump();
            Assert.Equal(20, translation.X);
            Assert.Equal(1, onTrack.Opacity);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-toggle-on.png");

            Find<RadioButton>(window, "Nav_hotkeys").IsChecked = true;
            CompletePageTransition(window);
            var ignore = Find<CheckBox>(window, "IgnoreFullscreen");
            ignore.IsChecked = false;
            PumpFor(240);
            Assert.Equal(0, ((TranslateTransform)((Grid)ignore.Template.FindName("Thumb", ignore)).RenderTransform).X);
            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            CompletePageTransition(window);
            Assert.Equal(20, translation.X);
            Assert.Equal(1, onTrack.Opacity);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Content_exits_down_then_enters_up_and_rapid_navigation_keeps_only_the_latest_page(bool light) => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, light).Window;
        try
        {
            var surface = Find<FrameworkElement>(window, "PageTransitionSurface");
            var translation = (TranslateTransform)surface.RenderTransform;
            var scroll = Find<ScrollViewer>(window, "PageScroll");
            var general = Find<StackPanel>(window, "Page_general");
            var search = Find<StackPanel>(window, "Page_search");
            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            window.Show();
            Pump();
            Assert.Equal(1, surface.Opacity);
            Assert.Equal(0, translation.Y);
            scroll.ScrollToVerticalOffset(80);
            Pump();
            var previousOffset = scroll.VerticalOffset;
            Assert.True(previousOffset > 0);

            Find<RadioButton>(window, "Nav_search").IsChecked = true;
            if (UiAnimationPolicy.Enabled)
            {
                Assert.True(general.IsVisible);
                Assert.False(search.IsVisible);
                Assert.False(scroll.IsHitTestVisible);
                Assert.Equal(previousOffset, scroll.VerticalOffset);
                PumpFor(35);
                Assert.InRange(translation.Y, 0.001, 13.999);
                Assert.InRange(surface.Opacity, 0.001, 0.999);
                if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                    Capture(window, $"settings-{(light ? "light" : "dark")}-content-exit.png");
                PumpFor(90);
                Assert.False(general.IsVisible);
                Assert.True(search.IsVisible);
                Assert.Equal(0, scroll.VerticalOffset);
                Assert.InRange(translation.Y, 0.001, 21.999);
                Assert.InRange(surface.Opacity, 0.001, 0.999);
                if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                    Capture(window, $"settings-{(light ? "light" : "dark")}-content-enter.png");
                var currentY = translation.Y;
                var currentOpacity = surface.Opacity;
                Find<RadioButton>(window, "Nav_music").IsChecked = true;
                Assert.Equal(currentY, translation.Y);
                Assert.Equal(currentOpacity, surface.Opacity);
                Find<RadioButton>(window, "Nav_about").IsChecked = true;
                Find<RadioButton>(window, "Nav_general").IsChecked = true;
                CompletePageTransition(window);
                Assert.True(general.IsVisible);
                Assert.False(search.IsVisible);
                Assert.False(Find<StackPanel>(window, "Page_music").IsVisible);
                Assert.False(Find<StackPanel>(window, "Page_about").IsVisible);
            }
            else
            {
                Assert.True(search.IsVisible);
                Assert.False(general.IsVisible);
            }
            Assert.Equal(1, surface.Opacity);
            Assert.Equal(0, translation.Y);
            Assert.Equal(0, ((TranslateTransform)Find<StackPanel>(window, "PageContent").RenderTransform).Y);
            Assert.True(scroll.IsHitTestVisible);
            Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(surface));

            Find<RadioButton>(window, "Nav_text").IsChecked = true;
            window.Hide();
            PumpFor(320);
            window.Show();
            Pump();
            Assert.True(Find<StackPanel>(window, "Page_text").IsVisible);
            Assert.Equal(1, surface.Opacity);
            Assert.Equal(0, translation.Y);
            Assert.True(scroll.IsHitTestVisible);
            Find<RadioButton>(window, "Nav_music").IsChecked = true;
            window.Close();
            PumpFor(320);
            Assert.Equal(1, surface.Opacity);
            Assert.Equal(0, translation.Y);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Shared_ripples_follow_settings_controls_pages_and_dialogs(bool light) => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, light).Window;
        try
        {
            window.Show();
            Pump();
            AdornerLayer Press(Control control)
            {
                Assert.True(ControlRippleHost.GetIsEnabled(control));
                control.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = control,
                });
                window.UpdateLayout();
                var layer = AdornerLayer.GetAdornerLayer(control)!;
                if (UiAnimationPolicy.Enabled)
                {
                    var adorner = Assert.Single(layer.GetAdorners(control)!);
                    Assert.False(adorner.IsHitTestVisible);
                    var clip = Assert.IsType<RectangleGeometry>(adorner.Clip);
                    Assert.Equal(control.RenderSize, clip.Rect.Size);
                    var chrome = control.Template.FindName("Chrome", control) as Border;
                    if (chrome is not null) Assert.Equal(chrome.CornerRadius.TopLeft, clip.RadiusX);
                    var canvas = Assert.IsType<Canvas>(VisualTreeHelper.GetChild(adorner, 0));
                    var ellipse = Assert.Single(canvas.Children.OfType<System.Windows.Shapes.Ellipse>());
                    var brush = Assert.IsType<RadialGradientBrush>(ellipse.Fill);
                    var foreground = Assert.IsType<SolidColorBrush>(control.Foreground);
                    Assert.Equal(PluginPalette.WithAlpha(foreground.Color, 0.21), brush.GradientStops[0].Color);
                }
                return layer;
            }

            var navigation = Find<RadioButton>(window, "Nav_general");
            var navigationLayer = Press(navigation);
            navigation.IsChecked = true;
            CompletePageTransition(window);
            var toggle = Find<CheckBox>(window, "Launch");
            var pageLayer = Press(toggle);
            Assert.NotSame(navigationLayer, pageLayer);
            Assert.Contains(VisualChildren(Find<ScrollViewer>(window, "PageScroll")), child => ReferenceEquals(child, pageLayer));
            toggle.IsEnabled = false;
            Assert.Null(pageLayer.GetAdorners(toggle));
            toggle.IsEnabled = true;
            Press(toggle);
            var combo = Find<ComboBox>(window, "AppLanguage");
            Press(VisualChildren(combo).OfType<ToggleButton>().Single());
            Find<RadioButton>(window, "Nav_hotkeys").IsChecked = true;
            CompletePageTransition(window);
            Assert.Null(pageLayer.GetAdorners(toggle));

            var edit = LogicalChildren(window).OfType<Button>().Single(button => Equals(button.Tag, "edit"));
            Press(edit);
            Click(window, "edit");
            Pump();
            Assert.Null(pageLayer.GetAdorners(edit));
            Assert.Null(navigationLayer.GetAdorners(navigation));
            var cancel = LogicalChildren(window).OfType<Button>().Single(button => Equals(button.Tag, "cancel"));
            var dialogLayer = Press(cancel);
            Click(window, "cancel");
            CompleteDialogTransition(window, open: false);
            Pump();
            Assert.Null(dialogLayer.GetAdorners(cancel));
            Click(window, "edit");
            Pump();
            Press(cancel);
            Click(window, "cancel");
            CompleteDialogTransition(window, open: false);
            window.Close();
            Assert.Null(dialogLayer.GetAdorners(cancel));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sidebar_selection_slides_and_retargets_from_its_current_position(bool light) => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, light).Window;
        try
        {
            window.Show();
            Pump();
            var host = Find<Grid>(window, "NavigationHost");
            var pill = Find<Border>(window, "NavigationSelection");
            var position = (TranslateTransform)pill.RenderTransform;
            TextBlock Label(string page, string name)
            {
                var item = Find<RadioButton>(window, "Nav_" + page);
                var label = ((Panel)item.Content).Children.OfType<ContentControl>().Single();
                return (TextBlock)label.Template.FindName(name, label);
            }
            void AssertLabel(string page, bool selected)
            {
                Assert.Equal(selected ? 0 : 1, Label(page, "RegularLabel").Opacity);
                Assert.Equal(selected ? 1 : 0, Label(page, "EmphasizedLabel").Opacity);
            }
            double Top(string page) => Find<RadioButton>(window, "Nav_" + page).TranslatePoint(new Point(), host).Y;
            AssertLabel("hotkeys", true);
            AssertLabel("general", false);
            Assert.Equal(Top("hotkeys"), position.Y, 1);
            Assert.False(pill.IsHitTestVisible);
            Assert.Equal(Find<RadioButton>(window, "Nav_hotkeys").ActualHeight, pill.Height);

            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            PumpFor(320);
            Assert.Equal(Top("general"), position.Y, 1);
            AssertLabel("general", true);
            var labelWidth = Label("general", "RegularLabel").ActualWidth;
            var labelOrigin = Label("general", "RegularLabel").TranslatePoint(new Point(), host);
            Find<RadioButton>(window, "Nav_music").IsChecked = true;
            Assert.True(Find<StackPanel>(window, UiAnimationPolicy.Enabled ? "Page_general" : "Page_music").IsVisible);
            if (SystemParameters.ClientAreaAnimation)
            {
                Assert.Equal(Top("general"), position.Y, 1);
                PumpFor(80);
                Assert.InRange(position.Y, Top("general") + 1, Top("music") - 1);
                Assert.InRange(Label("general", "RegularLabel").Opacity, 0.001, 0.999);
                Assert.InRange(Label("music", "EmphasizedLabel").Opacity, 0.001, 0.999);
                Assert.Equal(1, Label("music", "RegularLabel").Opacity + Label("music", "EmphasizedLabel").Opacity, 3);
                Assert.Equal(labelWidth, Label("general", "RegularLabel").ActualWidth);
                Assert.Equal(labelOrigin, Label("general", "RegularLabel").TranslatePoint(new Point(), host));
                if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                    Capture(window, $"settings-{(light ? "light" : "dark")}-navigation-moving.png");
                var before = position.Y;
                var weightBefore = Label("general", "RegularLabel").Opacity;
                Find<RadioButton>(window, "Nav_general").IsChecked = true;
                Assert.Equal(before, position.Y, 1);
                Assert.Equal(weightBefore, Label("general", "RegularLabel").Opacity, 3);
            }
            else
            {
                Assert.Equal(Top("music"), position.Y, 1);
                Find<RadioButton>(window, "Nav_general").IsChecked = true;
            }
            PumpFor(320);
            Assert.Equal(Top("general"), position.Y, 1);
            AssertLabel("general", true);
            AssertLabel("music", false);
            Click(window, "page:music");
            PumpFor(320);
            Assert.Equal(Top("music"), position.Y, 1);
            Assert.True(Find<RadioButton>(window, "Nav_music").IsChecked);
            AssertLabel("general", false);
            AssertLabel("music", true);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-navigation-selected.png");
            window.Width = window.MinWidth;
            Pump();
            Assert.Equal(host.ActualWidth, pill.ActualWidth, 1);
            Assert.Equal(Top("music"), position.Y, 1);
            Find<RadioButton>(window, "Nav_about").IsChecked = true;
            window.Hide();
            PumpFor(320);
            window.Show();
            PumpFor(320);
            Assert.Equal(Top("about"), position.Y, 1);
            AssertLabel("about", true);
            AssertLabel("music", false);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Wheel_scroll_moves_through_intermediate_positions_accumulates_and_reverses() => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, true).Window;
        try
        {
            window.Width = 960;
            window.Height = 590;
            window.Show();
            Find<RadioButton>(window, "Nav_about").IsChecked = true;
            CompletePageTransition(window);
            var scroll = Find<ScrollViewer>(window, "PageScroll");
            var translation = (TranslateTransform)Find<StackPanel>(window, "PageContent").RenderTransform;
            var step = SystemParameters.WheelScrollLines < 0 ? scroll.ViewportHeight * 2 : SystemParameters.WheelScrollLines * 32.0;
            var firstTarget = Math.Min(step, scroll.ScrollableHeight);
            var wheel = Wheel(scroll, -120);
            Assert.Equal(0, scroll.VerticalOffset);
            if (!SystemParameters.ClientAreaAnimation || step == 0)
            {
                Assert.False(wheel.Handled);
                return;
            }
            Assert.True(wheel.Handled);
            PumpFor(70);
            Assert.InRange(scroll.VerticalOffset, 0.1, firstTarget - 0.1);
            var intermediate = scroll.VerticalOffset;
            Assert.True(Wheel(scroll, -120).Handled);
            Assert.Equal(intermediate, scroll.VerticalOffset);
            PumpFor(70);
            Assert.True(scroll.VerticalOffset > intermediate);
            var beforeReverse = scroll.VerticalOffset;
            Assert.True(Wheel(scroll, 120).Handled);
            PumpFor(650);
            Assert.InRange(scroll.VerticalOffset, Math.Max(0, beforeReverse - step) - 1,
                Math.Max(0, beforeReverse - step) + 1);
            Assert.Equal(0, translation.Y);

            scroll.ScrollToTop();
            Pump();
            Wheel(scroll, -120);
            Wheel(scroll, -120);
            PumpFor(650);
            Assert.InRange(scroll.VerticalOffset, Math.Min(step * 2, scroll.ScrollableHeight) - 1,
                Math.Min(step * 2, scroll.ScrollableHeight) + 1);

            scroll.ScrollToTop();
            Pump();
            Wheel(scroll, -120);
            PumpFor(70);
            scroll.ScrollToVerticalOffset(110);
            Pump();
            PumpFor(250);
            Assert.Equal(110, scroll.VerticalOffset, 1);

            Wheel(scroll, -120);
            PumpFor(50);
            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            PumpFor(250);
            Assert.Equal(0, scroll.VerticalOffset);
            Assert.Equal(0, translation.Y);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Overscroll_springs_at_both_edges_and_resets_on_navigation_and_hide() => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, true).Window;
        try
        {
            window.Width = 960;
            window.Height = 590;
            window.Show();
            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            CompletePageTransition(window);
            var scroll = Find<ScrollViewer>(window, "PageScroll");
            var translation = (TranslateTransform)Find<StackPanel>(window, "PageContent").RenderTransform;
            var extent = scroll.ExtentHeight;
            var topWheel = Wheel(scroll, 120);
            PumpFor(90);
            if (!SystemParameters.ClientAreaAnimation)
            {
                Assert.False(topWheel.Handled);
                Assert.Equal(0, translation.Y);
                return;
            }
            Assert.True(topWheel.Handled);
            Assert.InRange(translation.Y, 1, 64);
            Assert.Equal(0, scroll.VerticalOffset);
            Assert.Equal(extent, scroll.ExtentHeight);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, "settings-bounce-top.png");
            PumpFor(1100);
            Assert.Equal(0, translation.Y);

            scroll.ScrollToBottom();
            Pump();
            var bottom = scroll.VerticalOffset;
            Assert.True(Wheel(scroll, -120).Handled);
            PumpFor(90);
            Assert.InRange(translation.Y, -64, -1);
            Assert.Equal(bottom, scroll.VerticalOffset);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, "settings-bounce-bottom.png");
            PumpFor(1100);
            Assert.Equal(0, translation.Y);

            Assert.True(Wheel(scroll, -120).Handled);
            PumpFor(50);
            Find<RadioButton>(window, "Nav_music").IsChecked = true;
            CompletePageTransition(window);
            Assert.Equal(0, translation.Y);
            Assert.False(Wheel(scroll, 120).Handled);

            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            CompletePageTransition(window);
            Assert.False(Wheel(Find<ComboBox>(window, "AppLanguage"), 120).Handled);
            scroll.ScrollToVerticalOffset(100);
            Pump();
            Assert.True(Wheel(scroll, 120).Handled);
            Assert.Equal(0, translation.Y);
            scroll.ScrollToTop();
            Pump();
            for (var i = 0; i < 10; i++) Wheel(scroll, 120);
            PumpFor(70);
            Assert.InRange(translation.Y, 1, 64);
            window.Hide();
            PumpFor(80);
            Assert.Equal(0, translation.Y);
            window.Show();
            Pump();
            Assert.Equal(0, translation.Y);
        }
        finally { window.Close(); }
    });

    private static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent,
        };
        target.RaiseEvent(args);
        return args;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scrolling_uses_the_full_viewport_and_overlay_fades_without_reserving_space(bool light) => OnSta(() =>
    {
        var window = new SettingsWindowView(TestUiStrings.English, light).Window;
        try
        {
            window.Width = 960;
            window.Height = 590;
            window.Show();
            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            CompletePageTransition(window);
            var scroll = Find<ScrollViewer>(window, "PageScroll");
            var viewport = (ScrollContentPresenter)scroll.Template.FindName("PART_ScrollContentPresenter", scroll);
            var bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
            var pageWidth = Find<StackPanel>(window, "PageContent").ActualWidth;
            Assert.Equal(0, viewport.TranslatePoint(new Point(), scroll).Y, 1);
            Assert.Equal(scroll.ActualHeight, viewport.ActualHeight, 1);
            Assert.Equal(scroll.ActualWidth, viewport.ActualWidth, 1);
            Assert.False(bar.IsHitTestVisible);

            scroll.ScrollToVerticalOffset(100);
            Pump();
            Assert.True(bar.IsHitTestVisible);
            PumpFor(OverlayScrollbarPolicy.FadeInMilliseconds + 40);
            Assert.True(bar.Opacity > 0.9);
            Assert.Equal(OverlayScrollbarPolicy.TrackWidthPixels, bar.ActualWidth);
            Assert.Equal(pageWidth, Find<StackPanel>(window, "PageContent").ActualWidth);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-general-middle.png");

            PumpFor(OverlayScrollbarPolicy.HideDelayMilliseconds + OverlayScrollbarPolicy.FadeOutMilliseconds + 80);
            Assert.False(bar.IsHitTestVisible);
            Assert.Equal(0, bar.Opacity, 2);
            Assert.Equal(pageWidth, Find<StackPanel>(window, "PageContent").ActualWidth);
            scroll.ScrollToBottom();
            Pump();
            var cleanup = Find<ComboBox>(window, "Cleanup");
            var cleanupTop = cleanup.TranslatePoint(new Point(), scroll).Y;
            Assert.True(cleanupTop >= 0);
            Assert.True(cleanupTop + cleanup.ActualHeight < viewport.ActualHeight);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-general-bottom.png");

            var root = (FrameworkElement)window.Content;
            var corner = Find<Border>(window, "ContentSurface").TranslatePoint(new Point(1, 1), root);
            var bitmap = Render(root);
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)corner.X, (int)corner.Y, 1, 1), pixel, 4, 0);
            var background = PluginPalette.Settings(light).Sidebar;
            Assert.Equal(new[] { background.B, background.G, background.R, background.A }, pixel);

            Find<RadioButton>(window, "Nav_music").IsChecked = true;
            CompletePageTransition(window);
            Assert.Equal(0, scroll.ScrollableHeight);
            Assert.False(bar.IsHitTestVisible);
            Assert.Equal(0, bar.Opacity);
            Assert.Equal(pageWidth, Find<StackPanel>(window, "PageContent").ActualWidth);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Preview_edits_survive_navigation_and_reset_or_close_discards_them() => OnSta(() =>
    {
        var view = new SettingsWindowView(TestUiStrings.English, true);
        var window = view.Window;
        try
        {
            window.Show();
            var provider = Find<ComboBox>(window, "Provider");
            var launch = Find<CheckBox>(window, "Launch");
            provider.SelectedIndex = 2;
            launch.IsChecked = false;
            Find<TextBox>(window, "Maximum").Text = "2500";
            Find<RadioButton>(window, "Nav_search").IsChecked = true;
            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            Assert.Equal(2, provider.SelectedIndex);
            Assert.False(launch.IsChecked);

            Find<RadioButton>(window, "Nav_about").IsChecked = true;
            Click(window, "reset");
            Assert.Equal(Visibility.Visible, Find<Border>(window, "DialogLayer").Visibility);
            Assert.False(Find<Grid>(window, "Workspace").IsEnabled);
            Click(window, "cancel");
            CompleteDialogTransition(window, open: false);
            Assert.Equal(2, provider.SelectedIndex);
            Click(window, "reset");
            Click(window, "confirm-reset");
            CompleteDialogTransition(window, open: false);
            Assert.True(launch.IsChecked);
            Assert.Equal(0, provider.SelectedIndex);
            Assert.Equal("1600", Find<TextBox>(window, "Maximum").Text);
            Assert.True(Find<Grid>(window, "Workspace").IsEnabled);

            Find<RadioButton>(window, "Nav_hotkeys").IsChecked = true;
            Click(window, "edit");
            Assert.False(Find<Button>(window, "SaveShortcut").IsEnabled);
            Click(window, "cancel");
            CompleteDialogTransition(window, open: false);
            provider.SelectedIndex = 1;
        }
        finally { window.Close(); }

        var fresh = new SettingsWindowView(TestUiStrings.English, true).Window;
        Assert.Equal(0, Find<ComboBox>(fresh, "Provider").SelectedIndex);
        fresh.Close();
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void All_pages_resolve_xaml_resources_and_render_at_default_and_minimum_size(bool light) => OnSta(() =>
    {
        var view = new SettingsWindowView(TestUiStrings.English, light);
        var window = view.Window;
        var bindingErrors = new StringWriter();
        using var listener = new TextWriterTraceListener(bindingErrors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            window.Show();
            foreach (var size in new[] { new Size(window.Width, window.Height), new Size(window.MinWidth, window.MinHeight) })
            {
                var width = size.Width;
                window.Width = width;
                window.Height = size.Height;
                foreach (var page in new[] { "general", "hotkeys", "search", "text", "music", "about" })
                {
                    Find<RadioButton>(window, "Nav_" + page).IsChecked = true;
                    CompletePageTransition(window);
                    window.UpdateLayout();
                    var content = Find<StackPanel>(window, "Page_" + page);
                    Assert.True(content.IsVisible);
                    Assert.All(content.Children.OfType<TextBlock>(), text => Assert.False(string.IsNullOrWhiteSpace(text.Text)));
                    Assert.All(VisualChildren(content).OfType<Control>(), control =>
                    {
                        if (control is ComboBox or TextBox or CheckBox)
                            Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(control)));
                    });
                    Assert.All(VisualChildren(content).OfType<TextBlock>(), text =>
                    {
                        if (text.Text.Length == 0) return;
                        var right = text.TranslatePoint(new Point(text.ActualWidth, 0), content).X;
                        Assert.True(right <= content.ActualWidth + 1, $"{page}: '{text.Text}' overflows at {width}");
                    });
                    if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                        Capture(window, $"settings-{(light ? "light" : "dark")}-{page}-{width}.png");
                }
            }
            Find<ScrollViewer>(window, "PageScroll").ScrollToBottom();
            Pump();
            Assert.True(Find<ScrollViewer>(window, "PageScroll").VerticalOffset > 0);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-about-bottom.png");
            Click(window, "reset");
            CompleteDialogTransition(window, open: true);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-reset-dialog.png");
            Click(window, "cancel");
            CompleteDialogTransition(window, open: false);
            Find<RadioButton>(window, "Nav_hotkeys").IsChecked = true;
            Click(window, "edit");
            CompleteDialogTransition(window, open: true);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-shortcut-dialog.png");
            Click(window, "cancel");
            CompleteDialogTransition(window, open: false);
            listener.Flush();
            Assert.Equal(string.Empty, bindingErrors.ToString());
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            window.Close();
        }
    });

    private static T Find<T>(Window window, string name) where T : FrameworkElement => (T)window.FindName(name);

    private static void Click(Window window, string tag)
    {
        var button = LogicalChildren(window).OfType<Button>().Single(button => Equals(button.Tag, tag));
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in LogicalChildren(child)) yield return nested;
        }
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in VisualChildren(child)) yield return nested;
        }
    }

    private static void Capture(Window window, string filename)
    {
        var root = (FrameworkElement)window.Content;
        CaptureVisual(root, filename);
    }

    private static void CaptureVisual(FrameworkElement root, string filename)
    {
        var bitmap = Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(TestOutputPaths.TempDirectory);
        using var stream = File.Create(Path.Combine(TestOutputPaths.TempDirectory, filename));
        encoder.Save(stream);
    }

    private static RenderTargetBitmap Render(FrameworkElement root)
    {
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void CompletePageTransition(Window window)
    {
        Pump();
        var elapsed = Stopwatch.StartNew();
        var scroll = Find<ScrollViewer>(window, "PageScroll");
        while (!scroll.IsHitTestVisible && elapsed.ElapsedMilliseconds < 2000) PumpFor(16);
        Assert.True(scroll.IsHitTestVisible, "The content transition did not finish.");
        Pump();
    }

    private static void CompleteDialogTransition(Window window, bool open)
    {
        Pump();
        var elapsed = Stopwatch.StartNew();
        var surface = Find<FrameworkElement>(window, "DialogMotionSurface");
        var layer = Find<Border>(window, "DialogLayer");
        bool Finished() => open ? surface.Opacity == 1 : layer.Visibility == Visibility.Collapsed;
        while (!Finished() && elapsed.ElapsedMilliseconds < 2000) PumpFor(16);
        Assert.True(Finished(), "The dialog transition did not finish.");
    }

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(40)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
