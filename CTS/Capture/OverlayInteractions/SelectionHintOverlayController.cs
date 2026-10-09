using System.Windows.Threading;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class SelectionHintOverlayController : IDisposable
{
    // Word gaps are wider than the hit tolerance, so hover flickers while the pointer crosses a line of text.
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);

    private readonly SelectionHintVisual _visual;
    private readonly UiStrings _strings;
    private readonly SelectionHint _idleHint;
    private readonly Action<Action> _changeTrayLayout;
    private readonly Func<bool> _animationsEnabled;
    private readonly Action<TimeSpan, Action>? _scheduleDelay;
    private readonly DispatcherTimer _timer;
    private long _revision;
    private bool _textHovered;
    private bool _zoomed;
    private bool _settled;
    private bool _disposed;

    internal SelectionHintOverlayController(
        SelectionHintVisual visual,
        UiStrings strings,
        SelectionHint idleHint,
        Action<Action> changeTrayLayout,
        Func<bool> animationsEnabled,
        Dispatcher dispatcher,
        Action<TimeSpan, Action>? scheduleDelay = null)
    {
        _visual = visual;
        _strings = strings;
        _idleHint = idleHint;
        _changeTrayLayout = changeTrayLayout;
        _animationsEnabled = animationsEnabled;
        _scheduleDelay = scheduleDelay;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _timer.Tick += OnTimer;
        Shown = idleHint;
        SelectionHintVisualPresenter.Show(visual, idleHint, strings, animate: false);
    }

    internal SelectionHint Shown { get; private set; }

    internal void SetTextHovered(bool hovered)
    {
        if (_disposed || _settled || hovered == _textHovered) return;
        _textHovered = hovered;
        ++_revision;
        _timer.Stop();
        if (Target == Shown) return;
        var delay = hovered ? ShowDelay : HideDelay;
        if (_scheduleDelay is not null)
        {
            var revision = _revision;
            _scheduleDelay(delay, () =>
            {
                if (!_disposed && revision == _revision) Apply();
            });
            return;
        }
        _timer.Interval = delay;
        _timer.Start();
    }

    // Shown at once: zoom changes on a deliberate gesture, unlike hover that flickers between words.
    internal void SetZoomed(bool zoomed)
    {
        if (_disposed || _settled || zoomed == _zoomed) return;
        _zoomed = zoomed;
        ++_revision;
        _timer.Stop();
        Apply();
    }

    internal void SettleForClosing()
    {
        _settled = true;
        ++_revision;
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimer;
    }

    private SelectionHint Target => _textHovered ? SelectionHint.AltLeftDragOverText
        : _zoomed ? SelectionHint.MiddleDragPan
        : _idleHint;

    private void OnTimer(object? sender, EventArgs e)
    {
        _timer.Stop();
        Apply();
    }

    private void Apply()
    {
        if (_settled || Target == Shown) return;
        Shown = Target;
        _changeTrayLayout(() => SelectionHintVisualPresenter.Show(_visual, Shown, _strings, _animationsEnabled()));
    }
}
