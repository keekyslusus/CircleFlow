using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ToastOverlayVisualTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_tone_uses_shared_surface_and_semantic_accent(bool lightTheme)
    {
        Assert.Null(RunOnSta(() =>
        {
            var theme = PluginPalette.For(lightTheme);
            VerifyTone(ToastTone.Neutral, theme.Toast.NeutralAccent);
            VerifyTone(ToastTone.Error, theme.Toast.ErrorAccent);
            VerifyTone(ToastTone.Success, theme.Toast.SuccessAccent);

            void VerifyTone(ToastTone tone, Color expectedAccent)
            {
                var visual = ToastOverlayVisualFactory.Create(
                    new ToastNotification("Message", tone),
                    lightTheme);
                Assert.Equal(theme.Toast.Surface, Assert.IsType<SolidColorBrush>(visual.Card.Background).Color);
                Assert.Equal(theme.Toast.Text, Assert.IsType<SolidColorBrush>(visual.Message.Foreground).Color);
                Assert.Equal(expectedAccent, Assert.IsType<SolidColorBrush>(visual.Card.BorderBrush).Color);
            }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Success_accent_reuses_music_primary(bool lightTheme)
    {
        var theme = PluginPalette.For(lightTheme);

        Assert.Equal(theme.MusicOverlay.Primary, theme.Toast.SuccessAccent);
    }

    [Fact]
    public void Card_exposes_layout_input_and_live_region_contract()
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = ToastOverlayVisualFactory.Create(
                new ToastNotification("Selection is too small", ToastTone.Error),
                lightTheme: false);

            Assert.Equal(360, visual.Card.MaxWidth);
            Assert.Equal(40, visual.Card.MinHeight);
            Assert.Equal(new Thickness(16, 10, 16, 10), visual.Card.Padding);
            Assert.Equal(new CornerRadius(20), visual.Card.CornerRadius);
            Assert.Equal(new Thickness(1), visual.Card.BorderThickness);
            Assert.Equal(13, visual.Message.FontSize);
            Assert.Equal(FontWeights.SemiBold, visual.Message.FontWeight);
            Assert.Equal(TextWrapping.Wrap, visual.Message.TextWrapping);
            Assert.False(visual.Slot.IsHitTestVisible);
            Assert.False(visual.Card.IsHitTestVisible);
            Assert.False(visual.Message.IsHitTestVisible);
            Assert.Equal(1, Panel.GetZIndex(visual.Slot));
            Assert.Equal(string.Empty, AutomationProperties.GetName(visual.Card));
            Assert.Equal("Selection is too small", AutomationProperties.GetName(visual.Message));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(visual.Message));
            Assert.Null(UIElementAutomationPeer.CreatePeerForElement(visual.Card));
            var peer = ToastOverlayVisualFactory.Announce(visual);
            Assert.IsType<TextBlockAutomationPeer>(peer);
            Assert.Same(peer, UIElementAutomationPeer.FromElement(visual.Message));

            ToastTransitions.BeginEntrance(visual.Card, animationsEnabled: false);
            Assert.NotSame(visual.Slot.RenderTransform, visual.Card.RenderTransform);
            var group = Assert.IsType<TransformGroup>(visual.Card.RenderTransform);
            Assert.Collection(
                group.Children,
                transform => Assert.IsType<ScaleTransform>(transform),
                transform => Assert.IsType<TranslateTransform>(transform));
        }));
    }

    [Fact]
    public void Localized_selection_message_resolves_from_english_xaml() =>
        Assert.Equal(
            "Selection is too small. Drag to select a larger area.",
            TestUiStrings.English.SelectionTooSmall);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Color_toast_uses_dynamic_swatch_theme_border_and_single_live_region(bool lightTheme)
    {
        Assert.Null(RunOnSta(() =>
        {
            var sample = new ToastColorSample(0x3A, 0x7B, 0xD5);
            var visual = ToastOverlayVisualFactory.Create(
                new ToastNotification(TestUiStrings.English.ColorCopied, ToastTone.Success, sample),
                lightTheme);

            Assert.Collection(
                visual.Message.Inlines,
                inline => Assert.Equal("Copied: ", Assert.IsType<Run>(inline).Text),
                inline =>
                {
                    var swatch = Assert.IsType<Border>(Assert.IsType<InlineUIContainer>(inline).Child);
                    Assert.Equal(16, swatch.Width);
                    Assert.Equal(16, swatch.Height);
                    Assert.Equal(new CornerRadius(4), swatch.CornerRadius);
                    Assert.Equal(new Thickness(1), swatch.BorderThickness);
                    Assert.Equal(
                        Color.FromRgb(0x3A, 0x7B, 0xD5),
                        Assert.IsType<SolidColorBrush>(swatch.Background).Color);
                    Assert.Equal(
                        PluginPalette.For(lightTheme).Toast.SwatchBorder,
                        Assert.IsType<SolidColorBrush>(swatch.BorderBrush).Color);
                    Assert.False(swatch.IsHitTestVisible);
                },
                inline => Assert.Equal(" #3A7BD5", Assert.IsType<Run>(inline).Text));
            Assert.Equal("Copied: #3A7BD5", AutomationProperties.GetName(visual.Message));
            Assert.IsType<TextBlockAutomationPeer>(ToastOverlayVisualFactory.Announce(visual));
        }));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    public void Extreme_swatches_keep_visible_border(byte red, byte green, byte blue)
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = ToastOverlayVisualFactory.Create(
                new ToastNotification("Copied:", ToastTone.Success, new ToastColorSample(red, green, blue)),
                lightTheme: red == 255);
            var container = Assert.IsType<InlineUIContainer>(visual.Message.Inlines.ElementAt(1));
            var swatch = Assert.IsType<Border>(container.Child);
            Assert.Equal(new Thickness(1), swatch.BorderThickness);
            Assert.NotNull(swatch.BorderBrush);
        }));
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
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive);
        return failure;
    }
}
