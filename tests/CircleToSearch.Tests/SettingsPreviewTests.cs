using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Shell.SettingsPreview;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SettingsPreviewTests
{
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
            foreach (var width in new[] { 1176, 960 })
            {
                window.Width = width;
                window.Height = width == 960 ? 590 : 760;
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
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(TestOutputPaths.TempDirectory);
        using var stream = File.Create(Path.Combine(TestOutputPaths.TempDirectory, filename));
        encoder.Save(stream);
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
