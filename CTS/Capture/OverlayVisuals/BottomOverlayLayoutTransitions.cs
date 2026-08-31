namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

public sealed class BottomOverlayLayoutTransitions : IDisposable
{
    private const double MinimumDeltaDips = 0.5;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(200);

    private readonly FrameworkElement _root;
    private readonly Panel _stack;
    private readonly Dictionary<FrameworkElement, TranslateTransform> _offsets = [];
    private readonly Dictionary<TranslateTransform, ActiveAnimation> _animations = [];
    private int _transactionDepth;
    private bool _transactionAnimationsEnabled;
    private bool _disposed;

    internal BottomOverlayLayoutTransitions(FrameworkElement root, Panel stack)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        EnsureSlotOffsets();
    }

    internal void Apply(Action mutation, bool animationsEnabled)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        Apply(
            () =>
            {
                mutation();
                return true;
            },
            animationsEnabled);
    }

    internal T Apply<T>(Func<T> mutation, bool animationsEnabled)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transactionDepth > 0)
        {
            _transactionAnimationsEnabled &= animationsEnabled;
            _transactionDepth++;
            try
            {
                return mutation();
            }
            finally
            {
                _transactionDepth--;
            }
        }

        _transactionDepth = 1;
        _transactionAnimationsEnabled = animationsEnabled;
        EnsureSlotOffsets();
        var first = animationsEnabled ? CapturePositions() : [];
        CancelAnimationsAndResetOffsets();
        try
        {
            var result = mutation();
            if (_disposed)
            {
                _root.UpdateLayout();
                return result;
            }
            EnsureSlotOffsets();
            PruneRemovedSlots();
            _root.UpdateLayout();
            if (_transactionAnimationsEnabled)
                AnimateFrom(first, CapturePositions());
            else
                CancelAnimationsAndResetOffsets();
            return result;
        }
        finally
        {
            _transactionDepth = 0;
            _transactionAnimationsEnabled = false;
        }
    }

    internal void Settle()
    {
        if (_disposed) return;
        CancelAnimationsAndResetOffsets();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAnimationsAndResetOffsets();
        _offsets.Clear();
    }

    private void EnsureSlotOffsets()
    {
        foreach (var slot in _stack.Children.OfType<FrameworkElement>())
        {
            if (_offsets.ContainsKey(slot)) continue;
            if (slot.RenderTransform is { } transform &&
                !ReferenceEquals(transform, Transform.Identity)) continue;
            var offset = new TranslateTransform();
            slot.RenderTransform = offset;
            _offsets.Add(slot, offset);
        }
    }

    private void PruneRemovedSlots()
    {
        var currentSlots = _stack.Children.OfType<FrameworkElement>().ToHashSet();
        foreach (var slot in _offsets.Keys.Where(slot => !currentSlots.Contains(slot)).ToArray())
            _offsets.Remove(slot);
    }

    private Dictionary<FrameworkElement, double> CapturePositions()
    {
        var positions = new Dictionary<FrameworkElement, double>();
        if (!_root.IsLoaded) return positions;
        foreach (var slot in _stack.Children.OfType<FrameworkElement>())
        {
            if (!_offsets.ContainsKey(slot) || !IsLayoutParticipant(slot)) continue;
            try
            {
                positions[slot] = slot.TransformToAncestor(_root).Transform(new Point()).Y;
            }
            catch (InvalidOperationException)
            {
                // A slot detached during a reentrant mutation does not participate.
            }
        }
        return positions;
    }

    private bool IsLayoutParticipant(FrameworkElement slot) =>
        slot.IsLoaded &&
        slot.IsVisible &&
        slot.ActualWidth > 0 &&
        slot.ActualHeight > 0 &&
        IsDescendantOfRoot(slot);

    private bool IsDescendantOfRoot(DependencyObject slot)
    {
        var current = slot;
        while (current is not null)
        {
            if (ReferenceEquals(current, _root)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void AnimateFrom(
        IReadOnlyDictionary<FrameworkElement, double> first,
        IReadOnlyDictionary<FrameworkElement, double> last)
    {
        foreach (var (slot, firstY) in first)
        {
            if (!last.TryGetValue(slot, out var lastY) || !_offsets.TryGetValue(slot, out var offset))
                continue;
            var delta = firstY - lastY;
            if (Math.Abs(delta) < MinimumDeltaDips) continue;

            offset.Y = delta;
            var animation = OverlayVisualResources.Animate(delta, 0, Duration);
            EventHandler? completed = null;
            completed = (_, _) => CompleteAnimation(offset);
            animation.Completed += completed;
            _animations[offset] = new ActiveAnimation(animation, completed);
            offset.BeginAnimation(
                TranslateTransform.YProperty,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void CompleteAnimation(TranslateTransform offset)
    {
        if (!_animations.Remove(offset, out var active)) return;
        active.Animation.Completed -= active.Completed;
        offset.BeginAnimation(TranslateTransform.YProperty, null);
        offset.Y = 0;
    }

    private void CancelAnimationsAndResetOffsets()
    {
        foreach (var (offset, active) in _animations)
        {
            active.Animation.Completed -= active.Completed;
            offset.BeginAnimation(TranslateTransform.YProperty, null);
            offset.Y = 0;
        }
        _animations.Clear();
        foreach (var offset in _offsets.Values)
        {
            offset.BeginAnimation(TranslateTransform.YProperty, null);
            offset.Y = 0;
        }
    }

    private sealed record ActiveAnimation(DoubleAnimation Animation, EventHandler Completed);
}
