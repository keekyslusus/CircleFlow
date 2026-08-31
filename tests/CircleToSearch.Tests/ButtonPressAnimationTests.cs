using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ButtonPressAnimationTests
{
    [Fact]
    public void Enabled_motion_centers_chrome_and_animates_both_axes_on_press_and_release()
    {
        var failure = RunOnSta(() =>
        {
            var button = CreateButton(animationsEnabled: true);
            button.ApplyTemplate();

            AssertChromeStartsAtNormalScale(button);
            var pressed = PressedTrigger(button);
            var setter = Assert.IsType<Setter>(Assert.Single(pressed.Setters));
            Assert.Equal(0.82, Assert.IsType<double>(setter.Value));
            AssertScaleAnimation(Assert.Single(pressed.EnterActions), 0.96, 80);
            AssertScaleAnimation(Assert.Single(pressed.ExitActions), 1, 140);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Disabled_motion_keeps_pressed_opacity_without_scale_storyboards()
    {
        var failure = RunOnSta(() =>
        {
            var button = CreateButton(animationsEnabled: false);
            button.ApplyTemplate();

            AssertChromeStartsAtNormalScale(button);
            var pressed = PressedTrigger(button);
            var setter = Assert.IsType<Setter>(Assert.Single(pressed.Setters));
            Assert.Equal(UIElement.OpacityProperty, setter.Property);
            Assert.Equal(0.82, Assert.IsType<double>(setter.Value));
            Assert.Empty(pressed.EnterActions);
            Assert.Empty(pressed.ExitActions);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Applying_template_preserves_button_layout_interaction_and_outer_transform()
    {
        var failure = RunOnSta(() =>
        {
            var outerTransform = new TranslateTransform(17, 23);
            var button = new Button
            {
                Width = 180,
                Height = 52,
                BorderThickness = new Thickness(2, 3, 4, 5),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Bottom,
                Focusable = false,
                RenderTransform = outerTransform,
                Content = "Content",
            };

            OverlayVisualResources.ApplyButtonTemplate(
                button,
                12,
                Colors.Gray,
                Colors.White,
                animationsEnabled: true);
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            button.Arrange(new Rect(button.DesiredSize));
            button.ApplyTemplate();

            Assert.Equal(180, button.ActualWidth);
            Assert.Equal(52, button.ActualHeight);
            Assert.Equal(new Thickness(2, 3, 4, 5), button.BorderThickness);
            Assert.Equal(HorizontalAlignment.Left, button.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Bottom, button.VerticalContentAlignment);
            Assert.False(button.Focusable);
            Assert.Same(outerTransform, button.RenderTransform);
        });

        Assert.Null(failure);
    }

    private static Button CreateButton(bool animationsEnabled)
    {
        var button = new Button();
        OverlayVisualResources.ApplyButtonTemplate(
            button,
            12,
            Colors.Gray,
            Colors.White,
            animationsEnabled);
        return button;
    }

    private static void AssertChromeStartsAtNormalScale(Button button)
    {
        var chrome = Assert.IsType<Border>(button.Template.FindName("Chrome", button));
        Assert.Equal(new Point(0.5, 0.5), chrome.RenderTransformOrigin);
        var scale = Assert.IsType<ScaleTransform>(chrome.RenderTransform);
        Assert.Equal(1, scale.ScaleX);
        Assert.Equal(1, scale.ScaleY);
    }

    private static System.Windows.Trigger PressedTrigger(Button button) =>
        Assert.Single(
            button.Template.Triggers.OfType<System.Windows.Trigger>(),
            trigger => trigger.Property == ButtonBase.IsPressedProperty);

    private static void AssertScaleAnimation(TriggerAction action, double expectedTo, int expectedDurationMs)
    {
        var begin = Assert.IsType<BeginStoryboard>(action);
        Assert.Equal(HandoffBehavior.SnapshotAndReplace, begin.HandoffBehavior);
        var animations = begin.Storyboard.Children.Cast<DoubleAnimation>().ToArray();
        Assert.Equal(2, animations.Length);
        Assert.All(animations, animation =>
        {
            Assert.Null(animation.From);
            Assert.Equal(expectedTo, animation.To);
            Assert.Equal(TimeSpan.FromMilliseconds(expectedDurationMs), animation.Duration.TimeSpan);
            Assert.Equal("Chrome", Storyboard.GetTargetName(animation));
            var easing = Assert.IsType<CubicEase>(animation.EasingFunction);
            Assert.Equal(EasingMode.EaseOut, easing.EasingMode);
        });
        Assert.Equal(
            ScaleTransform.ScaleXProperty,
            Storyboard.GetTargetProperty(animations[0]).PathParameters[1]);
        Assert.Equal(
            ScaleTransform.ScaleYProperty,
            Storyboard.GetTargetProperty(animations[1]).PathParameters[1]);
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}
