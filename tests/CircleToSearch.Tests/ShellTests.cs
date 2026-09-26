using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CircleToSearch.Interop;
using CircleToSearch.Shell;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ShellTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tray_menu_uses_rounded_surfaces_without_an_icon_gutter_and_keeps_keyboard_navigation(bool light) => OnSta(() =>
    {
        var menu = TrayMenuView.Create();
        var owner = new Window { Width = 300, Height = 200 };
        owner.Show();
        owner.Activate();
        menu.PlacementTarget = owner;
        TrayMenuView.ApplyTheme(menu, light);
        foreach (var text in new[] { TestUiStrings.English.TrayOpen, TestUiStrings.English.TraySettings,
                     TestUiStrings.English.TraySupport, TestUiStrings.English.TrayExit })
            menu.Items.Add(new MenuItem { Header = text });
        try
        {
            menu.IsOpen = true;
            Pump();
            menu.UpdateLayout();
            var surface = (Border)menu.Template.FindName("MenuSurface", menu);
            Assert.Equal(new CornerRadius(9), surface.CornerRadius);
            Assert.True(surface.ActualWidth >= 200);
            var first = (MenuItem)menu.Items[0];
            var second = (MenuItem)menu.Items[1];
            foreach (MenuItem item in menu.Items)
            {
                Assert.Null(item.Icon);
                Assert.Equal(34, item.ActualHeight);
                var highlight = (Border)item.Template.FindName("Highlight", item);
                var presenter = Assert.IsType<ContentPresenter>(highlight.Child);
                Assert.InRange(presenter.TranslatePoint(new Point(), item).X, 11, 13);
                Assert.True(presenter.ActualWidth > 150);
            }
            first.Focus();
            Assert.True(first.IsHighlighted);
            Assert.True(first.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Down)));
            Assert.True(second.IsKeyboardFocused);
            Assert.True(second.IsHighlighted);
            Pump();
            menu.UpdateLayout();
            var selected = (Border)second.Template.FindName("Highlight", second);
            Assert.Equal(PluginPalette.Settings(light).Selected, ((System.Windows.Media.SolidColorBrush)selected.Background).Color);
            if (Environment.GetEnvironmentVariable("CTS_SETTINGS_PREVIEW") == "1")
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),
                    (int)Math.Ceiling(menu.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(menu);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                System.IO.Directory.CreateDirectory(TestOutputPaths.TempDirectory);
                using var output = System.IO.File.Create(System.IO.Path.Combine(TestOutputPaths.TempDirectory,
                    $"tray-menu-{(light ? "light" : "dark")}.png"));
                encoder.Save(output);
            }
            TrayMenuView.ApplyTheme(menu, !light);
            Assert.Equal(PluginPalette.Settings(!light).Paper, ((System.Windows.Media.SolidColorBrush)menu.Background).Color);
            menu.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(menu), 0, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent,
            });
            Pump();
            Assert.False(menu.IsOpen);
            menu.IsOpen = true;
            Pump();
            Assert.True(menu.IsOpen);
        }
        finally { menu.IsOpen = false; owner.Close(); }
    });

    [Fact]
    public void Emergency_tray_removal_does_not_need_the_owner_dispatcher() => OnSta(() =>
    {
        var opens = 0;
        using var tray = new TrayIcon(new AppPaths(AppContext.BaseDirectory).TrayIconPath, TestUiStrings.English,
            Dispatcher.CurrentDispatcher, CreateLog(), () => { opens++; return Task.CompletedTask; }, () => null,
            () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask);
        Assert.True(tray.IsAdded);
        var removal = new Thread(tray.RemoveForShutdown) { IsBackground = true };
        removal.Start();
        Assert.True(removal.Join(TimeSpan.FromSeconds(2)));
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = tray.WindowHandle, Id = 1,
            Tip = string.Empty, Info = string.Empty, InfoTitle = string.Empty,
        };
        Assert.False(TrayNativeMethods.Shell_NotifyIconW(2, ref data));
        SendTrayEvent(tray, 0x203);
        TrayNativeMethods.PostMessageW(tray.WindowHandle, tray.TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero);
        Pump();
        Assert.Equal(0, opens);
        Assert.False(TrayNativeMethods.Shell_NotifyIconW(2, ref data));
    });

    [Fact]
    public void Tray_icon_uses_the_small_icon_size_for_the_current_dpi_and_follows_dpi_changes() => OnSta(() =>
    {
        using var tray = new TrayIcon(new AppPaths(AppContext.BaseDirectory).TrayIconPath, TestUiStrings.English,
            Dispatcher.CurrentDispatcher, CreateLog(), () => Task.CompletedTask, () => null,
            () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask);
        Assert.Equal(TrayNativeMethods.GetSystemMetricsForDpi(49, TrayNativeMethods.GetDpiForSystem()), tray.IconSize);
        // Windows refuses to post WM_DPICHANGED because lParam carries a pointer to the suggested rectangle.
        var suggested = Marshal.AllocHGlobal(16);
        try { SendMessageW(tray.WindowHandle, 0x02E0, new IntPtr((144 << 16) | 144), suggested); }
        finally { Marshal.FreeHGlobal(suggested); }
        Assert.Equal(TrayNativeMethods.GetSystemMetricsForDpi(49, 144), tray.IconSize);
        Assert.True(tray.IsAdded);
    });

    [Fact]
    public void Settings_preview_is_lazy_and_reused_until_closed() => OnSta(() =>
    {
        var harness = new TestSettingsWindow();
        using var settings = new SingleWindowController(Dispatcher.CurrentDispatcher, () => harness.CreateView().Window);
        Assert.Null(settings.CurrentWindow);
        settings.Show();
        var first = settings.CurrentWindow!;
        Assert.Equal(TestUiStrings.English.SettingsWindowTitle, first.Title);
        Assert.Equal(WindowStyle.SingleBorderWindow, first.WindowStyle);
        Assert.Null(System.Windows.Shell.WindowChrome.GetWindowChrome(first));
        Assert.NotNull(first.Icon);
        Assert.IsType<Grid>(first.Content);
        Assert.NotNull(first.DataContext);
        first.WindowState = WindowState.Minimized;
        settings.Show();
        Assert.Same(first, settings.CurrentWindow);
        Assert.Equal(WindowState.Normal, first.WindowState);
        first.Close();
        Assert.Null(settings.CurrentWindow);
        settings.Show();
        Assert.NotSame(first, settings.CurrentWindow);
        settings.Dispose();
        settings.Show();
        Assert.Null(settings.CurrentWindow);
    });

    [Fact]
    public void Notifications_marshal_to_dispatcher_keep_actions_and_ignore_work_after_disposal() => OnSta(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var presenter = new NotificationPresenter(dispatcher, TestUiStrings.English, CreateLog());
        var calls = 0;
        Task.Run(() => presenter.ShowMessageWithButton("test", "recognized track", "Open in Shazam", () =>
        {
            dispatcher.VerifyAccess();
            calls++;
            throw new InvalidOperationException("failed action");
        })).GetAwaiter().GetResult();
        Pump();
        var window = Assert.Single(presenter.Windows);
        Assert.True(window.IsVisible);
        var panel = Assert.IsType<StackPanel>(window.Content);
        Assert.Equal("recognized track", Assert.IsType<TextBlock>(panel.Children[0]).Text);
        var buttons = Assert.IsType<StackPanel>(panel.Children[1]);
        var action = Assert.IsType<Button>(buttons.Children[0]);
        Assert.Equal("Open in Shazam", action.Content);
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, calls);
        Assert.Empty(presenter.Windows);
        presenter.ShowError("test", "failure");
        Pump();
        Assert.Single(presenter.Windows);
        presenter.ShowMessage("test", "queued before exit");
        presenter.Dispose();
        presenter.ShowMessage("test", "late callback");
        Pump();
        Assert.Empty(presenter.Windows);
    });

    [Fact]
    public void Tray_menu_uses_the_current_app_language_each_time_it_opens() => OnSta(() =>
    {
        var prefix = string.Empty;
        var strings = new UiStrings(key => prefix + TestUiStrings.EnglishValues[key]);
        using var tray = new TrayIcon(new AppPaths(AppContext.BaseDirectory).TrayIconPath, strings,
            Dispatcher.CurrentDispatcher, CreateLog(), () => Task.CompletedTask, () => "Ctrl+K",
            () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask);
        prefix = "ru:";
        SendTrayEvent(tray, 0x7B);
        Pump();
        Assert.Equal(new[] { "ru:Open (Ctrl+K)", "ru:Settings", "ru:Support the project", "ru:Exit" },
            tray.Menu.Items.Cast<MenuItem>().Select(item => (string)item.Header));
        tray.CloseMenu();
        Pump();
    });

    [Fact]
    public void Tray_routes_only_intended_events_restores_registration_and_disables_queued_callbacks() => OnSta(() =>
    {
        var calls = new int[4];
        Task Command(int index)
        {
            Dispatcher.CurrentDispatcher.VerifyAccess();
            calls[index]++;
            return index == 2 ? Task.FromException(new InvalidOperationException("failed command")) : Task.CompletedTask;
        }
        string? hotkey = null;
        using var tray = new TrayIcon(new AppPaths(AppContext.BaseDirectory).TrayIconPath, TestUiStrings.English,
            Dispatcher.CurrentDispatcher, CreateLog(), () => Command(0), () => hotkey,
            () => Command(1), () => Command(2), () => Command(3));
        Assert.Equal(new[] { "Open", "Settings", "Support the project", "Exit" },
            tray.Menu.Items.Cast<MenuItem>().Select(item => (string)item.Header));
        Assert.True(tray.IsAdded);
        SendTrayEvent(tray, 0x202);
        SendTrayEvent(tray, 0x400);
        Pump();
        Assert.Equal(0, calls[0]);
        SendTrayEvent(tray, 0x203);
        Pump();
        Assert.Equal(1, calls[0]);
        foreach (MenuItem item in tray.Menu.Items) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump();
        Assert.Equal(new[] { 2, 1, 1, 1 }, calls);
        hotkey = "Ctrl+Alt+Space";
        SendTrayEvent(tray, 0x7B);
        Pump();
        Assert.True(tray.Menu.IsOpen);
        Assert.Equal("Open (Ctrl+Alt+Space)", ((MenuItem)tray.Menu.Items[0]).Header);
        TrayNativeMethods.PostMessageW(tray.WindowHandle, 0x1C, IntPtr.Zero, IntPtr.Zero);
        Pump();
        Assert.False(tray.Menu.IsOpen);
        hotkey = null;
        SendTrayEvent(tray, 0x7B);
        Pump();
        Assert.Equal("Open", ((MenuItem)tray.Menu.Items[0]).Header);
        tray.CloseMenu();
        Pump();

        // Simulate Explorer dropping this registration before broadcasting TaskbarCreated.
        var data = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = tray.WindowHandle, Id = 1,
            Tip = string.Empty, Info = string.Empty, InfoTitle = string.Empty };
        Assert.True(TrayNativeMethods.Shell_NotifyIconW(2, ref data));
        TrayNativeMethods.PostMessageW(tray.WindowHandle, tray.TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero);
        Pump();
        Assert.True(tray.IsAdded);
        ((MenuItem)tray.Menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        tray.Dispose();
        Pump();
        Assert.Equal(2, calls[0]);
        Assert.False(tray.IsAdded);
        Assert.False(TrayNativeMethods.Shell_NotifyIconW(2, ref data));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Support_uses_only_the_fixed_project_url_and_reports_launch_failure(bool success)
    {
        string? target = null;
        var notifier = new TestPluginNotifier();
        var urlOpening = new UrlOpeningService(
            url => { target = url; return success; }, notifier, TestUiStrings.English, CreateLog());
        new ProjectSupport(urlOpening).Open();
        Assert.Equal("https://ko-fi.com/keekys", target);
        Assert.Equal(success ? 0 : 1, notifier.Errors.Count);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static void SendTrayEvent(TrayIcon tray, int notification) =>
        TrayNativeMethods.PostMessageW(tray.WindowHandle, TrayIcon.CallbackMessage, IntPtr.Zero, new IntPtr((1 << 16) | notification));

    private static PluginLog CreateLog() => new(Path.Combine(TestOutputPaths.TempDirectory, "shell-" + Guid.NewGuid().ToString("N")));

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
