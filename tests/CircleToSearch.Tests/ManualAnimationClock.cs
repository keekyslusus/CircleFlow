using System.Diagnostics;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Threading;

namespace CircleToSearch.Tests;

// WPF animations and CompositionTarget.Rendering read the dispatcher's internal TimeManager clock.
// Replacing it lets UI tests move time explicitly, so results do not depend on machine load.
internal sealed class ManualAnimationClock : IDisposable
{
    private const int FrameMilliseconds = 16;
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly object _mediaContext;
    private readonly MethodInfo _render;
    private readonly object _timeManager;
    private readonly PropertyInfo _clock;
    private readonly object _systemClock;
    private readonly ManualTime _time;

    private ManualAnimationClock(object mediaContext, MethodInfo render, object timeManager, PropertyInfo clock, object systemClock, ManualTime time)
    {
        _mediaContext = mediaContext;
        _render = render;
        _timeManager = timeManager;
        _clock = clock;
        _systemClock = systemClock;
        _time = time;
    }

    public static ManualAnimationClock Install()
    {
        var core = typeof(Visual).Assembly;
        var mediaContextType = Required(core.GetType("System.Windows.Media.MediaContext"));
        var mediaContext = Required(Required(mediaContextType.GetMethod("From", Members)).Invoke(null, [Dispatcher.CurrentDispatcher]));
        var render = Required(mediaContextType.GetMethod("RenderMessageHandler", Members));
        var timeManager = Required(Required(mediaContextType.GetProperty("TimeManager", Members)).GetValue(mediaContext));
        var clock = Required(timeManager.GetType().GetProperty("Clock", Members));
        var clockInterface = Required(core.GetType("System.Windows.Media.Animation.IClock"));
        var systemClock = Required(clock.GetValue(timeManager));
        var time = (ManualTime)DispatchProxy.Create(clockInterface, typeof(ManualTime));
        // Continue from the current system time: moving the global clock backwards would corrupt running clocks.
        time.Now = (TimeSpan)Required(Required(clockInterface.GetProperty("CurrentTime")).GetValue(systemClock));
        clock.SetValue(timeManager, time);
        return new ManualAnimationClock(mediaContext, render, timeManager, clock, systemClock, time);
    }

    public void Advance(int milliseconds)
    {
        for (var remaining = milliseconds; remaining > 0; remaining -= FrameMilliseconds)
        {
            _time.Now += TimeSpan.FromMilliseconds(Math.Min(FrameMilliseconds, remaining));
            _render.Invoke(_mediaContext, [null]);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    // Bounded by real time, not animation time: a condition may also wait on thread-pool continuations,
    // which reach the dispatcher only after a real delay.
    public bool AdvanceUntil(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.ElapsedMilliseconds < timeoutMilliseconds) Advance(FrameMilliseconds);
        return condition();
    }

    public void Dispose() => _clock.SetValue(_timeManager, _systemClock);

    private static T Required<T>(T? value) where T : class =>
        value ?? throw new InvalidOperationException("WPF timing internals changed; update ManualAnimationClock.");

    public class ManualTime : DispatchProxy
    {
        public TimeSpan Now { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Now;
    }
}
