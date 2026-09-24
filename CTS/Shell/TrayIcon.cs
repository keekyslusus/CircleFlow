using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CircleToSearch.Interop;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell;

internal sealed class TrayIcon : IDisposable
{
    internal const int CallbackMessage = 0x8001;
    private const uint IconId = 1;
    private readonly Dispatcher _dispatcher;
    private readonly PluginLog _log;
    private const int DpiChangedMessage = 0x02E0;
    private const int SmallIconWidthMetric = 49;
    private readonly HwndSource _source;
    private readonly string _iconPath;
    private IntPtr _icon;
    private readonly IntPtr _iconWindow;
    private readonly uint _taskbarCreated;
    private readonly DispatcherTimer _retry;
    private readonly Func<Task> _open;
    private readonly Func<string?> _openHotkey;
    private readonly UiStrings _strings;
    private readonly MenuItem _openItem;
    private NotifyIconData _data;
    private bool _added;
    private bool _disposed;
    private int _removedForShutdown;

    public TrayIcon(string iconPath, UiStrings strings, Dispatcher dispatcher, PluginLog log,
        Func<Task> open, Func<string?> openHotkey, Func<Task> settings, Func<Task> support, Func<Task> exit)
    {
        dispatcher.VerifyAccess();
        _dispatcher = dispatcher;
        _log = log;
        _open = open;
        _openHotkey = openHotkey;
        _strings = strings;
        _taskbarCreated = TrayNativeMethods.RegisterWindowMessageW("TaskbarCreated");
        if (_taskbarCreated == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _iconPath = iconPath;
        _icon = LoadIcon(TrayNativeMethods.GetDpiForSystem());
        if (_icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            // Explorer broadcasts TaskbarCreated to top-level windows, not HWND_MESSAGE windows.
            _source = new HwndSource(new HwndSourceParameters(strings.PluginTitle)
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80,
                Width = 0, Height = 0, ParentWindow = IntPtr.Zero,
            });
            _source.AddHook(WindowProc);
            _iconWindow = _source.Handle;
            Menu = TrayMenuView.Create();
            _openItem = AddItem(strings.TrayOpen, open);
            AddItem(strings.TraySettings, settings);
            AddItem(strings.TraySupport, support);
            AddItem(strings.TrayExit, exit);
            Menu.Closed += OnMenuClosed;
            _data = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle, Id = IconId,
                Flags = 0x1 | 0x2 | 0x4 | 0x80, CallbackMessage = CallbackMessage,
                Icon = _icon, Tip = strings.PluginTitle, Info = string.Empty, InfoTitle = string.Empty,
            };
            _retry = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
                OnRetry, dispatcher);
            TryAdd();
        }
        catch
        {
            _retry?.Stop();
            if (_added) TrayNativeMethods.Shell_NotifyIconW(2, ref _data);
            _source?.Dispose();
            TrayNativeMethods.DestroyIcon(_icon);
            throw;
        }
    }

    internal ContextMenu Menu { get; }
    internal IntPtr WindowHandle => _source.Handle;
    internal bool IsAdded => _added;
    internal uint TaskbarCreatedMessage => _taskbarCreated;
    internal int IconSize { get; private set; }

    // Loading the exact small-icon size lets Windows pick the matching ICO frame instead of downscaling 32px.
    private IntPtr LoadIcon(uint dpi)
    {
        var size = TrayNativeMethods.GetSystemMetricsForDpi(SmallIconWidthMetric, dpi);
        var icon = TrayNativeMethods.LoadImageW(IntPtr.Zero, _iconPath, 1, size, size, 0x10);
        if (icon != IntPtr.Zero) IconSize = size;
        return icon;
    }

    private void ReloadIcon(uint dpi)
    {
        var icon = LoadIcon(dpi);
        if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var previous = _icon;
        _icon = icon;
        _data.Icon = icon;
        if (_added) TrayNativeMethods.Shell_NotifyIconW(1, ref _data);
        TrayNativeMethods.DestroyIcon(previous);
    }

    private MenuItem AddItem(string text, Func<Task> command)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => QueueCommand(command);
        Menu.Items.Add(item);
        return item;
    }

    private void QueueCommand(Func<Task> command)
    {
        if (_disposed || Volatile.Read(ref _removedForShutdown) != 0) return;
        CloseMenu();
        _dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_disposed || Volatile.Read(ref _removedForShutdown) != 0) return;
            try { await command(); }
            catch (Exception exception) { _log.SafeError(nameof(TrayIcon), "tray-command", exception); }
        }));
    }

    private void TryAdd()
    {
        if (_disposed || Volatile.Read(ref _removedForShutdown) != 0) return;
        if (!_added) _added = TrayNativeMethods.Shell_NotifyIconW(0, ref _data);
        if (!_added) { _retry.Start(); return; }
        _data.Version = 4;
        if (!TrayNativeMethods.Shell_NotifyIconW(4, ref _data))
        {
            TrayNativeMethods.Shell_NotifyIconW(2, ref _data);
            _added = false;
            _retry.Start();
            return;
        }
        _retry.Stop();
    }

    private void OnRetry(object? sender, EventArgs args)
    {
        try { TryAdd(); }
        catch (Exception exception) { _log.SafeError(nameof(TrayIcon), "restore-tray", exception); }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed || Volatile.Read(ref _removedForShutdown) != 0) return IntPtr.Zero;
        try
        {
            if ((uint)message == _taskbarCreated)
            {
                _added = false;
                CloseMenu();
                TryAdd();
            }
            else if (message == CallbackMessage && ((lParam.ToInt64() >> 16) & 0xFFFF) == IconId)
            {
                handled = true;
                var notification = (uint)(lParam.ToInt64() & 0xFFFF);
                if (notification is 0x203 or 0x401) QueueCommand(_open);
                else if (notification == 0x7B) ShowMenu();
            }
            else if (message == 0x1C && wParam == IntPtr.Zero) CloseMenu();
            else if (message == DpiChangedMessage)
            {
                // The hidden window has no visual content, so WPF must not resize it to the suggested rectangle.
                handled = true;
                ReloadIcon((uint)(wParam.ToInt64() & 0xFFFF));
            }
        }
        catch (Exception exception) { _log.SafeError(nameof(TrayIcon), "tray-window-message", exception); }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        TrayMenuView.ApplyTheme(Menu, SystemTheme.IsLight());
        _openItem.Header = OpenHeader();
        TrayNativeMethods.SetForegroundWindow(_source.Handle);
        Menu.IsOpen = true;
        Menu.Focus();
    }

    // The hotkey can change in settings or fail to register, so the label is refreshed on every open.
    private string OpenHeader() =>
        _openHotkey() is { Length: > 0 } hotkey ? _strings.TrayOpenWithHotkey(hotkey) : _strings.TrayOpen;

    public void CloseMenu()
    {
        _dispatcher.VerifyAccess();
        Menu.IsOpen = false;
    }

    private void OnMenuClosed(object? sender, RoutedEventArgs args) =>
        TrayNativeMethods.PostMessageW(_source.Handle, 0, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _retry.Stop();
        _retry.Tick -= OnRetry;
        Menu.Closed -= OnMenuClosed;
        try { Menu.IsOpen = false; }
        finally
        {
            try { RemoveForShutdown(); }
            finally
            {
                _added = false;
                try { _source.Dispose(); }
                finally { TrayNativeMethods.DestroyIcon(_icon); }
            }
        }
    }

    public void RemoveForShutdown()
    {
        Interlocked.Exchange(ref _removedForShutdown, 1);
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _iconWindow, Id = IconId,
            Tip = string.Empty, Info = string.Empty, InfoTitle = string.Empty,
        };
        TrayNativeMethods.Shell_NotifyIconW(2, ref data);
    }
}
