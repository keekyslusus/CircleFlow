using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Shell.SettingsPreview;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SettingsPreviewTests
{
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
            Pump();
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
            Pump();
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
            Pump();
            Assert.Equal(0, translation.Y);
            Assert.False(Wheel(scroll, 120).Handled);

            Find<RadioButton>(window, "Nav_general").IsChecked = true;
            Pump();
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
            Pump();
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
            Pump();
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
            Assert.Equal(2, provider.SelectedIndex);
            Click(window, "reset");
            Click(window, "confirm-reset");
            Assert.True(launch.IsChecked);
            Assert.Equal(0, provider.SelectedIndex);
            Assert.Equal("1600", Find<TextBox>(window, "Maximum").Text);
            Assert.True(Find<Grid>(window, "Workspace").IsEnabled);

            Find<RadioButton>(window, "Nav_hotkeys").IsChecked = true;
            Click(window, "edit");
            Assert.False(Find<Button>(window, "SaveShortcut").IsEnabled);
            Click(window, "cancel");
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
                    Pump();
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
            Pump();
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-reset-dialog.png");
            Click(window, "cancel");
            Find<RadioButton>(window, "Nav_hotkeys").IsChecked = true;
            Click(window, "edit");
            Pump();
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
                Capture(window, $"settings-{(light ? "light" : "dark")}-shortcut-dialog.png");
            Click(window, "cancel");
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
