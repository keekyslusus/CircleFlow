namespace CircleToSearch.Capture;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Ui;

// A layout switch rolls the old code up and out while the new one rises in, and the tag pops briefly,
// so the change is noticed from the corner of the eye while typing.
public sealed class PromptLanguageTag
{
    private const double TagHeight = 24;
    private const double RollDistance = 12;
    private const double PopScale = 1.12;
    private const double EntranceScale = 0.72;
    private static readonly TimeSpan RollDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan PopDuration = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(220);
    private static readonly DependencyProperty[] ScaleAxes = [ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty];
    private readonly FloatingToolbarPalette _palette;
    private readonly TextBlock _current;
    private readonly TextBlock _outgoing;
    private readonly TranslateTransform _currentShift = new();
    private readonly TranslateTransform _outgoingShift = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly SolidColorBrush _background;
    private int _generation;

    internal PromptLanguageTag(FloatingToolbarPalette palette)
    {
        _palette = palette;
        _current = CreateLabel(palette, _currentShift);
        _outgoing = CreateLabel(palette, _outgoingShift);
        _outgoing.Opacity = 0;
        // Centered even while the tag is narrower than a code during a width change, so it never jumps aside.
        var labels = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        labels.Children.Add(_outgoing);
        labels.Children.Add(_current);
        _background = new SolidColorBrush(palette.Tag);
        Root = new Border
        {
            Child = labels,
            Background = _background,
            CornerRadius = new CornerRadius(TagHeight / 2),
            Height = TagHeight,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            RenderTransform = _scale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
    }

    internal Border Root { get; }
    internal string? Label => Root.Visibility == Visibility.Visible ? _current.Text : null;
    internal string OutgoingLabel => _outgoing.Text;

    internal static string? CodeFor(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return null;
        try { return CultureInfo.GetCultureInfo(languageTag).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch (CultureNotFoundException) { return languageTag.Split('-')[0].ToUpperInvariant(); }
    }

    internal void Set(string? languageTag, bool animate)
    {
        var label = CodeFor(languageTag);
        var previous = Label;
        if (label == previous) return;
        _generation++;
        var fromWidth = Root.ActualWidth;
        Settle();
        if (label is null)
        {
            _current.Text = string.Empty;
            Root.Visibility = Visibility.Collapsed;
            return;
        }
        _current.Text = label;
        Root.Visibility = Visibility.Visible;
        if (!animate) return;
        if (previous is null)
        {
            Appear();
            return;
        }
        _outgoing.Text = previous;
        Roll(_outgoing, _outgoingShift, 0, -RollDistance, 1, 0);
        Roll(_current, _currentShift, RollDistance, 0, 0, 1);
        ResizeTo(fromWidth);
        Pop();
    }

    // The outgoing code still takes part in measuring, so the width is driven explicitly until it is cleared.
    private void ResizeTo(double fromWidth)
    {
        _current.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toWidth = _current.DesiredSize.Width + Root.Padding.Left + Root.Padding.Right;
        var width = OverlayVisualResources.Animate(fromWidth > 0 ? fromWidth : toWidth, toWidth, RollDuration);
        var generation = _generation;
        width.Completed += (_, _) =>
        {
            if (generation != _generation) return;
            _outgoing.Text = string.Empty;
            Root.BeginAnimation(FrameworkElement.WidthProperty, null);
        };
        Root.BeginAnimation(FrameworkElement.WidthProperty, width);
    }

    private void Appear()
    {
        Root.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(0, 1, EntranceDuration));
        foreach (var axis in ScaleAxes)
            _scale.BeginAnimation(axis, OverlayVisualResources.Animate(EntranceScale, 1, EntranceDuration));
    }

    private static void Roll(TextBlock label, TranslateTransform shift, double fromY, double toY,
        double fromOpacity, double toOpacity)
    {
        label.Opacity = toOpacity;
        label.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(fromOpacity, toOpacity, RollDuration));
        shift.Y = toY;
        shift.BeginAnimation(TranslateTransform.YProperty, OverlayVisualResources.Animate(fromY, toY, RollDuration));
    }

    private void Pop()
    {
        foreach (var axis in ScaleAxes)
        {
            var scale = new DoubleAnimationUsingKeyFrames { Duration = PopDuration };
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(PopScale, KeyTime.FromPercent(0.35),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            _scale.BeginAnimation(axis, scale);
        }
        var color = new ColorAnimationUsingKeyFrames { Duration = PopDuration };
        color.KeyFrames.Add(new EasingColorKeyFrame(_palette.TagHighlight, KeyTime.FromPercent(0.35),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        color.KeyFrames.Add(new EasingColorKeyFrame(_palette.Tag, KeyTime.FromPercent(1),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        _background.BeginAnimation(SolidColorBrush.ColorProperty, color);
    }

    private void Settle()
    {
        Root.BeginAnimation(FrameworkElement.WidthProperty, null);
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Opacity = 1;
        foreach (var axis in ScaleAxes)
        {
            _scale.BeginAnimation(axis, null);
            _scale.SetValue(axis, 1d);
        }
        _background.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _background.Color = _palette.Tag;
        foreach (var (label, shift) in new[] { (_current, _currentShift), (_outgoing, _outgoingShift) })
        {
            label.BeginAnimation(UIElement.OpacityProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
        }
        _current.Opacity = 1;
        _outgoing.Opacity = 0;
        _outgoing.Text = string.Empty;
    }

    private static TextBlock CreateLabel(FloatingToolbarPalette palette, TranslateTransform shift) => new()
    {
        Foreground = OverlayVisualResources.Frozen(palette.TagText),
        FontFamily = OverlayVisualResources.Font,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        RenderTransform = shift,
    };
}
