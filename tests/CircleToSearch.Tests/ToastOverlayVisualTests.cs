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

    [Fact]
    public void Card_exposes_layout_input_and_live_region_contract()
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = ToastOverlayVisualFactory.Create(
                new ToastNotification("Selection is too small", ToastTone.Error),
                lightTheme: false);

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
