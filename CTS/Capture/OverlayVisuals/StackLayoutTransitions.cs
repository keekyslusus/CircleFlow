namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

public sealed class StackLayoutTransitions : IDisposable
{
    private const double MinimumDeltaDips = 0.5;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(200);

    private readonly FrameworkElement _root;
    private readonly StackPanel _stack;
    private readonly DependencyProperty _axis;
    private readonly Dictionary<FrameworkElement, TranslateTransform> _offsets = [];
    private readonly Dictionary<TranslateTransform, ActiveAnimation> _animations = [];
    private int _transactionDepth;
    private bool _transactionAnimationsEnabled;
    private bool _disposed;

    internal StackLayoutTransitions(FrameworkElement root, StackPanel stack)
    {
        _root = root;
        _stack = stack;
        _axis = stack.Orientation == Orientation.Horizontal
            ? TranslateTransform.XProperty
            : TranslateTransform.YProperty;
        EnsureSlotOffsets();
    }

    internal void Apply(Action mutation, bool animationsEnabled)
    {
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
                var position = slot.TransformToAncestor(_root).Transform(new Point());
                positions[slot] = _axis == TranslateTransform.XProperty ? position.X : position.Y;
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
        foreach (var (slot, firstPosition) in first)
        {
            if (!last.TryGetValue(slot, out var lastPosition) || !_offsets.TryGetValue(slot, out var offset))
                continue;
            var delta = firstPosition - lastPosition;
            if (Math.Abs(delta) < MinimumDeltaDips) continue;

            offset.SetValue(_axis, delta);
            var animation = OverlayVisualResources.Animate(delta, 0, Duration);
            EventHandler? completed = null;
            completed = (_, _) => CompleteAnimation(offset);
            animation.Completed += completed;
            _animations[offset] = new ActiveAnimation(animation, completed);
            offset.BeginAnimation(
                _axis,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void CompleteAnimation(TranslateTransform offset)
    {
        if (!_animations.Remove(offset, out var active)) return;
        active.Animation.Completed -= active.Completed;
        ResetOffset(offset);
    }

    private void CancelAnimationsAndResetOffsets()
    {
        foreach (var (offset, active) in _animations)
        {
            active.Animation.Completed -= active.Completed;
            ResetOffset(offset);
        }
        _animations.Clear();
        foreach (var offset in _offsets.Values)
        {
            ResetOffset(offset);
        }
    }

    private void ResetOffset(TranslateTransform offset)
    {
        offset.BeginAnimation(_axis, null);
        offset.SetValue(_axis, 0.0);
    }

    private sealed record ActiveAnimation(DoubleAnimation Animation, EventHandler Completed);
}
