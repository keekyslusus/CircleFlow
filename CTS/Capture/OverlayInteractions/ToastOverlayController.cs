namespace CircleToSearch.Capture.OverlayInteractions;

using System.Windows;
using System.Windows.Threading;

internal sealed class ToastOverlayController : IDisposable
{
    private const double AdjacentGapDips = 8;
    private const double ContentGapDips = 16;

    private readonly BottomOverlayVisual _bottom;
    private readonly bool _lightTheme;
    private readonly Func<bool> _animationsEnabled;
    private readonly TimeProvider _time;
    private readonly Action<ToastTone> _shown;
    private readonly List<ActiveToast> _active = [];
    private long _nextId;
    private bool _closing;
    private bool _disposed;

    internal ToastOverlayController(
        BottomOverlayVisual bottom,
        bool lightTheme,
        Func<bool> animationsEnabled,
        TimeProvider time,
        Action<ToastTone>? shown = null)
    {
        _bottom = bottom;
        _lightTheme = lightTheme;
        _animationsEnabled = animationsEnabled;
        _time = time;
        _shown = shown ?? (_ => { });
    }

    internal int ActiveCount => _active.Count;
    internal IReadOnlyList<ToastOverlayVisual> ActiveVisuals =>
        _active.Select(entry => entry.Visual).ToArray();

    internal void Show(ToastNotification notification)
        => ShowCore(notification, null, announce: true);

    internal void ShowOrUpdate(string key, ToastNotification notification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var existing = _active.FirstOrDefault(entry => entry.Key == key);
        // Updating a toast already on screen stays silent, so a changing status does not chime on every step.
        var announce = existing is null;
        if (existing is not null)
        {
            existing.Lifetime?.Dispose();
            existing.Exit?.Dispose();
            ToastTransitions.Settle(existing.Visual.Card);
            _bottom.LayoutTransitions.Apply(() =>
            {
                _active.Remove(existing);
                _bottom.Stack.Children.Remove(existing.Visual.Slot);
                RepairMargins();
            }, _animationsEnabled());
        }
        ShowCore(notification, key, announce);
    }

    private void ShowCore(ToastNotification notification, string? key, bool announce)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_closing) return;
        if (announce) _shown(notification.Tone);
        var animationsEnabled = _animationsEnabled();
        var visual = ToastOverlayVisualFactory.Create(notification, _lightTheme);
        var entry = new ActiveToast(++_nextId, visual, key);

        _bottom.LayoutTransitions.Apply(() =>
        {
            _active.Add(entry);
            _bottom.Stack.Children.Insert(
                _active.Count - 1,
                visual.Slot);
            RepairMargins();
        }, animationsEnabled);
        ToastOverlayVisualFactory.Announce(visual);
        ToastTransitions.BeginEntrance(visual.Card, animationsEnabled);
        var dispatcher = visual.Slot.Dispatcher;
        var id = entry.Id;
        // Timer callbacks arrive on the thread pool; the id lets a late callback find nothing after removal.
        entry.Lifetime = _time.CreateTimer(
            _ => dispatcher.BeginInvoke(DispatcherPriority.Background, () => OnLifetimeElapsed(id)),
            null,
            notification.Duration,
            Timeout.InfiniteTimeSpan);
    }

    internal void SettleForClosing()
    {
        if (_disposed) return;
        _closing = true;
        ClearOwnedVisuals();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearOwnedVisuals();
    }

    private void OnLifetimeElapsed(long id)
    {
        if (_disposed) return;
        var entry = _active.FirstOrDefault(candidate => candidate.Id == id);
        if (entry is null || entry.Exit is not null) return;
        entry.Lifetime?.Dispose();
        entry.Lifetime = null;

        var animationsEnabled = _animationsEnabled();
        var exit = ToastTransitions.BeginExit(
            entry.Visual.Card,
            animationsEnabled,
            () => CompleteExit(id, animationsEnabled));
        if (!exit.IsCompleted && _active.Contains(entry))
            entry.Exit = exit;
        else
            exit.Dispose();
    }

    private void CompleteExit(long id, bool animationsEnabled)
    {
        if (_disposed) return;
        var entry = _active.FirstOrDefault(candidate => candidate.Id == id);
        if (entry is null) return;
        var exit = entry.Exit;
        entry.Exit = null;
        exit?.Dispose();
        _bottom.LayoutTransitions.Apply(() =>
        {
            _active.Remove(entry);
            _bottom.Stack.Children.Remove(entry.Visual.Slot);
            RepairMargins();
        }, animationsEnabled);
    }

    private void RepairMargins()
    {
        for (var index = 0; index < _active.Count; index++)
        {
            var bottom = index == _active.Count - 1 ? ContentGapDips : AdjacentGapDips;
            _active[index].Visual.Slot.Margin = new Thickness(0, 0, 0, bottom);
        }
    }

    private void ClearOwnedVisuals()
    {
        foreach (var entry in _active)
        {
            entry.Lifetime?.Dispose();
            entry.Exit?.Dispose();
            entry.Exit = null;
            ToastTransitions.Settle(entry.Visual.Card);
            _bottom.Stack.Children.Remove(entry.Visual.Slot);
        }
        _active.Clear();
    }

    private sealed class ActiveToast(long id, ToastOverlayVisual visual, string? key)
    {
        internal long Id { get; } = id;
        internal string? Key { get; } = key;
        internal ToastOverlayVisual Visual { get; } = visual;
        internal ITimer? Lifetime { get; set; }
        internal CardTransitions.ExitHandle? Exit { get; set; }
    }
}
