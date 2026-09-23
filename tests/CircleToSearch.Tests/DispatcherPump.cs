using System.Diagnostics;
using System.Windows.Threading;

namespace CircleToSearch.Tests;

// Real-time pumping for work ManualAnimationClock does not drive:
// DispatcherTimer intervals, thread-pool continuations and WebView2.
internal static class DispatcherPump
{
    public static bool Until(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.ElapsedMilliseconds < timeoutMilliseconds) For(10);
        return condition();
    }

    public static void For(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
