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
    private readonly List<ActiveToast> _active = [];
    private long _nextId;
    private bool _closing;
    private bool _disposed;

    internal ToastOverlayController(
        BottomOverlayVisual bottom,
        bool lightTheme,
        Func<bool> animationsEnabled)
    {
        _bottom = bottom ?? throw new ArgumentNullException(nameof(bottom));
        _lightTheme = lightTheme;
        _animationsEnabled = animationsEnabled ?? throw new ArgumentNullException(nameof(animationsEnabled));
    }

    internal int ActiveCount => _active.Count;
    internal IReadOnlyList<ToastOverlayVisual> ActiveVisuals =>
        _active.Select(entry => entry.Visual).ToArray();

    internal void Show(ToastNotification notification)
        => ShowCore(notification, null);

    internal void ShowOrUpdate(string key, ToastNotification notification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var existing = _active.FirstOrDefault(entry => entry.Key == key);
        if (existing is not null)
        {
            StopTimer(existing.Timer);
            existing.Exit?.Dispose();
            ToastTransitions.Settle(existing.Visual.Card);
            _bottom.LayoutTransitions.Apply(() =>
            {
                _active.Remove(existing);
                _bottom.Stack.Children.Remove(existing.Visual.Slot);
                RepairMargins();
            }, _animationsEnabled());
        }
        ShowCore(notification, key);
    }

    private void ShowCore(ToastNotification notification, string? key)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_closing) return;
        var animationsEnabled = _animationsEnabled();
        var visual = ToastOverlayVisualFactory.Create(notification, _lightTheme);
        var timer = new DispatcherTimer { Interval = notification.Duration };
        var entry = new ActiveToast(++_nextId, visual, timer, key);
        timer.Tick += OnLifetimeElapsed;
        timer.Tag = entry.Id;

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
        timer.Start();
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

    private void OnLifetimeElapsed(object? sender, EventArgs e)
    {
        if (sender is not DispatcherTimer timer || timer.Tag is not long id) return;
        StopTimer(timer);
        if (_disposed) return;
        var entry = _active.FirstOrDefault(candidate => candidate.Id == id);
        if (entry is null || entry.Exit is not null) return;

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
            StopTimer(entry.Timer);
            entry.Exit?.Dispose();
            entry.Exit = null;
            ToastTransitions.Settle(entry.Visual.Card);
            _bottom.Stack.Children.Remove(entry.Visual.Slot);
        }
        _active.Clear();
    }

    private void StopTimer(DispatcherTimer timer)
    {
        timer.Stop();
        timer.Tick -= OnLifetimeElapsed;
        timer.Tag = null;
    }

    private sealed class ActiveToast(long id, ToastOverlayVisual visual, DispatcherTimer timer, string? key)
    {
        internal long Id { get; } = id;
        internal string? Key { get; } = key;
        internal ToastOverlayVisual Visual { get; } = visual;
        internal DispatcherTimer Timer { get; } = timer;
        internal CardTransitions.ExitHandle? Exit { get; set; }
    }
}
