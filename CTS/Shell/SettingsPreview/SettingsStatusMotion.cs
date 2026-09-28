using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsStatusMotion
{
    private const double HiddenOffset = 8;
    private readonly FrameworkElement _banner;
    private readonly TranslateTransform _translation = new();
    private int _version;
    private bool _shown;

    internal SettingsStatusMotion(FrameworkElement banner)
    {
        _banner = banner;
        banner.RenderTransform = _translation;
    }

    internal void Show()
    {
        if (_shown) return;
        _shown = true;
        if (_banner.Visibility != Visibility.Visible) SetValues(0, HiddenOffset);
        _banner.Visibility = Visibility.Visible;
        Animate(show: true);
    }

    internal void Hide()
    {
        if (!_shown) return;
        _shown = false;
        Animate(show: false);
    }

    private void Animate(bool show)
    {
        var version = ++_version;
        if (!UiAnimationPolicy.Enabled || !_banner.IsVisible)
        {
            Finish(show);
            return;
        }
        var duration = TimeSpan.FromMilliseconds(show ? 200 : 140);
        var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        SettingsMotionValue.Animate(_translation, TranslateTransform.YProperty, show ? 0 : HiddenOffset, duration, easing);
        SettingsMotionValue.Animate(_banner, UIElement.OpacityProperty, show ? 1 : 0, duration, easing, () =>
        {
            if (version == _version) Finish(show);
        });
    }

    private void Finish(bool show)
    {
        SetValues(show ? 1 : 0, show ? 0 : HiddenOffset);
        if (!show) _banner.Visibility = Visibility.Collapsed;
    }

    private void SetValues(double opacity, double y)
    {
        _version++;
        _banner.BeginAnimation(UIElement.OpacityProperty, null);
        _translation.BeginAnimation(TranslateTransform.YProperty, null);
        _banner.Opacity = opacity;
        _translation.Y = y;
    }
}
