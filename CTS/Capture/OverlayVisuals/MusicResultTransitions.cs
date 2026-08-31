namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

internal static class MusicResultTransitions
{
    internal static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);

    internal static void BeginEntrance(FrameworkElement card, bool animationsEnabled)
    {
        ArgumentNullException.ThrowIfNull(card);
        var transforms = Prepare(card);
        StopAnimations(card, transforms, preserveCurrentValues: false);
        card.Opacity = 1;
        transforms.Scale.ScaleX = 1;
        transforms.Scale.ScaleY = 1;
        transforms.Translate.Y = 0;

        if (!animationsEnabled) return;

        Animate(card, UIElement.OpacityProperty, 0, 1, EntranceDuration);
        Animate(transforms.Scale, ScaleTransform.ScaleXProperty, 0.96, 1, EntranceDuration);
        Animate(transforms.Scale, ScaleTransform.ScaleYProperty, 0.96, 1, EntranceDuration);
        Animate(transforms.Translate, TranslateTransform.YProperty, 8, 0, EntranceDuration);
    }

    internal static ExitHandle BeginExit(
        FrameworkElement card,
        bool animationsEnabled,
        Action completed)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(completed);
        var transforms = Prepare(card);
        var currentOpacity = card.Opacity;
        var currentScaleX = transforms.Scale.ScaleX;
        var currentScaleY = transforms.Scale.ScaleY;
        var currentOffset = transforms.Translate.Y;
        StopAnimations(card, transforms, preserveCurrentValues: true);

        if (!animationsEnabled)
        {
            SetExitState(card, transforms);
            var completedHandle = new ExitHandle(card, transforms, null, completed);
            completedHandle.CompleteSynchronously();
            return completedHandle;
        }

        card.Opacity = 0;
        transforms.Scale.ScaleX = 0.98;
        transforms.Scale.ScaleY = 0.98;
        transforms.Translate.Y = -8;
        Animate(card, UIElement.OpacityProperty, currentOpacity, 0, ExitDuration);
        Animate(transforms.Scale, ScaleTransform.ScaleXProperty, currentScaleX, 0.98, ExitDuration);
        Animate(transforms.Scale, ScaleTransform.ScaleYProperty, currentScaleY, 0.98, ExitDuration);
        var completionAnimation = OverlayVisualResources.Animate(
            currentOffset,
            -8,
            ExitDuration);
        var handle = new ExitHandle(card, transforms, completionAnimation, completed);
        transforms.Translate.BeginAnimation(
            TranslateTransform.YProperty,
            completionAnimation,
            HandoffBehavior.SnapshotAndReplace);
        return handle;
    }

    internal static (ScaleTransform Scale, TranslateTransform Translate) GetTransforms(
        FrameworkElement card)
    {
        var transforms = Prepare(card);
        return (transforms.Scale, transforms.Translate);
    }

    private static TransitionTransforms Prepare(FrameworkElement card)
    {
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        if (card.RenderTransform is TransformGroup group &&
            group.Children.Count == 2 &&
            group.Children[0] is ScaleTransform scale &&
            group.Children[1] is TranslateTransform translate)
            return new TransitionTransforms(scale, translate);

        var ownedScale = new ScaleTransform(1, 1);
        var ownedTranslate = new TranslateTransform();
        var ownedGroup = new TransformGroup();
        ownedGroup.Children.Add(ownedScale);
        ownedGroup.Children.Add(ownedTranslate);
        card.RenderTransform = ownedGroup;
        return new TransitionTransforms(ownedScale, ownedTranslate);
    }

    private static DoubleAnimation Animate(
        IAnimatable target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration)
    {
        var animation = OverlayVisualResources.Animate(from, to, duration);
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        return animation;
    }

    private static void StopAnimations(
        FrameworkElement card,
        TransitionTransforms transforms,
        bool preserveCurrentValues)
    {
        var opacity = card.Opacity;
        var scaleX = transforms.Scale.ScaleX;
        var scaleY = transforms.Scale.ScaleY;
        var offset = transforms.Translate.Y;
        card.BeginAnimation(UIElement.OpacityProperty, null);
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transforms.Translate.BeginAnimation(TranslateTransform.YProperty, null);
        if (!preserveCurrentValues) return;
        card.Opacity = opacity;
        transforms.Scale.ScaleX = scaleX;
        transforms.Scale.ScaleY = scaleY;
        transforms.Translate.Y = offset;
    }

    private static void SetExitState(FrameworkElement card, TransitionTransforms transforms)
    {
        card.Opacity = 0;
        transforms.Scale.ScaleX = 0.98;
        transforms.Scale.ScaleY = 0.98;
        transforms.Translate.Y = -8;
    }

    internal readonly record struct TransitionTransforms(
        ScaleTransform Scale,
        TranslateTransform Translate);

    internal sealed class ExitHandle : IDisposable
    {
        private FrameworkElement? _card;
        private TransitionTransforms _transforms;
        private DoubleAnimation? _completionAnimation;
        private Action? _completed;

        internal ExitHandle(
            FrameworkElement card,
            TransitionTransforms transforms,
            DoubleAnimation? completionAnimation,
            Action completed)
        {
            _card = card;
            _transforms = transforms;
            _completionAnimation = completionAnimation;
            _completed = completed;
            if (_completionAnimation is not null) _completionAnimation.Completed += OnCompleted;
        }

        internal bool IsCompleted { get; private set; }

        public void Dispose()
        {
            if (_completionAnimation is not null)
                _completionAnimation.Completed -= OnCompleted;
            _completionAnimation = null;
            if (_card is { } card)
                StopAnimations(card, _transforms, preserveCurrentValues: true);
            _card = null;
            _completed = null;
        }

        internal void CompleteSynchronously() => Complete();

        private void OnCompleted(object? sender, EventArgs e) => Complete();

        private void Complete()
        {
            if (IsCompleted) return;
            IsCompleted = true;
            var completed = _completed;
            _completed = null;
            if (_completionAnimation is not null)
                _completionAnimation.Completed -= OnCompleted;
            _completionAnimation = null;
            completed?.Invoke();
        }
    }
}
