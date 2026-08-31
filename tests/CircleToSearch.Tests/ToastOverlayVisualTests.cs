using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
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
    public void Every_tone_uses_its_semantic_theme_palette(bool lightTheme)
    {
        Assert.Null(RunOnSta(() =>
        {
            var theme = PluginPalette.For(lightTheme);
            VerifyTone(ToastTone.Neutral, theme.Toast.Neutral);
            VerifyTone(ToastTone.Error, theme.Toast.Error);
            VerifyTone(ToastTone.Success, theme.Toast.Success);

            void VerifyTone(ToastTone tone, ToastTonePalette expected)
            {
                var visual = ToastOverlayVisualFactory.Create(
                    new ToastNotification("Message", tone),
                    lightTheme);
                Assert.Equal(expected.Surface, Assert.IsType<SolidColorBrush>(visual.Card.Background).Color);
                Assert.Equal(expected.Text, Assert.IsType<SolidColorBrush>(visual.Message.Foreground).Color);
                Assert.Equal(expected.Border, Assert.IsType<SolidColorBrush>(visual.Card.BorderBrush).Color);
            }
        }));
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
