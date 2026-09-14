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
    [Fact]
    public void Emergency_tray_removal_does_not_need_the_owner_dispatcher() => OnSta(() =>
    {
        var opens = 0;
        using var tray = new TrayIcon(new AppPaths(AppContext.BaseDirectory).TrayIconPath, TestUiStrings.English,
            Dispatcher.CurrentDispatcher, CreateLog(), () => { opens++; return Task.CompletedTask; },
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
    public void Settings_is_lazy_blank_and_reused_until_closed() => OnSta(() =>
    {
        using var settings = new SettingsWindowController(Dispatcher.CurrentDispatcher, TestUiStrings.English);
        Assert.Null(settings.CurrentWindow);
        settings.Show();
        var first = settings.CurrentWindow!;
        Assert.Equal(TestUiStrings.English.SettingsWindowTitle, first.Title);
        Assert.Null(first.Content);
        Assert.Null(first.DataContext);
        first.WindowState = WindowState.Minimized;
        settings.Show();
        Assert.Same(first, settings.CurrentWindow);
        Assert.Equal(WindowState.Normal, first.WindowState);
        settings.Hide();
        Assert.False(first.IsVisible);
        settings.Show();
        Assert.Same(first, settings.CurrentWindow);
        Assert.True(first.IsVisible);
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
    public void Tray_routes_only_intended_events_restores_registration_and_disables_queued_callbacks() => OnSta(() =>
    {
        var calls = new int[4];
        Task Command(int index)
        {
            Dispatcher.CurrentDispatcher.VerifyAccess();
            calls[index]++;
            return index == 2 ? Task.FromException(new InvalidOperationException("failed command")) : Task.CompletedTask;
        }
        using var tray = new TrayIcon(new AppPaths(AppContext.BaseDirectory).TrayIconPath, TestUiStrings.English,
            Dispatcher.CurrentDispatcher, CreateLog(), () => Command(0), () => Command(1), () => Command(2), () => Command(3));
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
        SendTrayEvent(tray, 0x7B);
        Pump();
        Assert.True(tray.Menu.IsOpen);
        TrayNativeMethods.PostMessageW(tray.WindowHandle, 0x1C, IntPtr.Zero, IntPtr.Zero);
        Pump();
        Assert.False(tray.Menu.IsOpen);

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
        new ProjectSupport(url => { target = url; return success; }, notifier, TestUiStrings.English).Open();
        Assert.Equal("https://ko-fi.com/keekys", target);
        Assert.Equal(success ? 0 : 1, notifier.Errors.Count);
    }

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
