using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CircleToSearch.Interop;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class KeyboardInputLanguageSourceTests
{
    [Fact]
    public void Captured_transient_russian_layout_resolves_after_overlay_activation_when_installed()
    {
        var layouts = new IntPtr[GetKeyboardLayoutList(0, null)];
        GetKeyboardLayoutList(layouts.Length, layouts);
        var russian = layouts.FirstOrDefault(layout => layout.ToInt64() == 0x04192000);
        var english = layouts.FirstOrDefault(layout => layout.ToInt64() == 0x04090409);
        if (russian == IntPtr.Zero || english == IntPtr.Zero) return;

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var prior = NativeMethods.GetKeyboardLayout(0);
            var window = new Window { Width = 100, Height = 80, ShowInTaskbar = false };
            string? initial = null;
            var changes = new List<string?>();
            using var source = new KeyboardInputLanguageSource(changes.Add, tag => initial = tag);
            try
            {
                window.Loaded += (_, _) => source.Attach(window,
                    new KeyboardLanguageSnapshot(russian, null));
                window.Show();
                window.Activate();
                if (initial is null)
                {
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                    timer.Start();
                    Dispatcher.PushFrame(frame);
                }
                Assert.StartsWith("ru-", initial);
                Assert.Empty(changes);
                NativeMethods.ActivateKeyboardLayout(english, 0);
                var switchFrame = new DispatcherFrame();
                var switchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                switchTimer.Tick += (_, _) => { switchTimer.Stop(); switchFrame.Continue = false; };
                switchTimer.Start();
                Dispatcher.PushFrame(switchFrame);
                Assert.Contains(changes, tag => tag is not null && tag.StartsWith("en-", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                source.Dispose();
                window.Close();
                if (prior != IntPtr.Zero) NativeMethods.ActivateKeyboardLayout(prior, 0);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));
        Assert.False(thread.IsAlive);
        Assert.Null(failure);
    }

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, IntPtr[]? layouts);

    [Fact]
    public void Ordinary_layout_identifiers_resolve_without_treating_transient_identifiers_as_locales()
    {
        Assert.Equal("en-US", KeyboardInputLanguageSource.TagFromLayout(0x0409));
        Assert.Equal("ru-RU", KeyboardInputLanguageSource.TagFromLayout(0x0419));
        Assert.Null(KeyboardInputLanguageSource.TagFromLayout(0));
        Assert.Null(KeyboardInputLanguageSource.TagFromLayout(0x2000));
        Assert.Null(KeyboardInputLanguageSource.TagFromLayout(0x2400));
    }
}
