using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ResultStateCardVisualFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_builds_the_state_card_with_the_selected_palette(bool lightTheme)
    {
        var failure = RunOnSta(() =>
        {
            var palette = PluginPalette.For(lightTheme).ResultStateCard;
            var geometry = OverlayVisualResources.FrozenGeometry("M0 0 10 0 10 10Z");
            var visual = ResultStateCardVisualFactory.Create(
                new ResultStateCardOptions(
                    geometry,
                    "Result message",
                    "Result state",
                    "Close result",
                    () => { },
                    new ResultStateCardAction("Try again", () => { })),
                palette);

            Assert.Equal(340, visual.Card.Width);
            Assert.Equal(540, visual.Card.MaxWidth);
            Assert.Equal(new CornerRadius(18), visual.Card.CornerRadius);
            Assert.Equal(new Thickness(14, 12, 14, 12), visual.Card.Padding);
            Assert.Equal(new Thickness(1), visual.Card.BorderThickness);
            Assert.Equal(palette.Surface, BrushColor(visual.Card.Background));
            Assert.Equal(palette.Border, BrushColor(visual.Card.BorderBrush));
            Assert.Equal("Result state", AutomationProperties.GetName(visual.Card));
            var shadow = Assert.IsType<DropShadowEffect>(visual.Card.Effect);
            Assert.Equal(PluginPalette.OpaqueBlack, shadow.Color);
            Assert.Equal(20, shadow.BlurRadius);
            Assert.Equal(10, shadow.ShadowDepth);
            Assert.Equal(-90, shadow.Direction);
            Assert.Equal(palette.ShadowOpacity, shadow.Opacity);

            Assert.Same(geometry, visual.Icon.Data);
            Assert.Equal(22, visual.Icon.Width);
            Assert.Equal(22, visual.Icon.Height);
            Assert.Equal(palette.OnSecondaryContainer, BrushColor(visual.Icon.Fill));
            Assert.Equal("Result message", visual.Message.Text);
            Assert.Equal(palette.Text, BrushColor(visual.Message.Foreground));
            Assert.Equal(230, visual.Message.Width);

            var root = Assert.IsType<Grid>(visual.Card.Child);
            Assert.Equal(2, root.RowDefinitions.Count);
            Assert.Equal(3, root.Children.Count);
            var messageRow = Assert.IsType<StackPanel>(root.Children[1]);
            var iconContainer = Assert.IsType<Border>(messageRow.Children[0]);
            Assert.Equal(44, iconContainer.Width);
            Assert.Equal(44, iconContainer.Height);
            Assert.Equal(new CornerRadius(22), iconContainer.CornerRadius);
            Assert.Equal(palette.SecondaryContainer, BrushColor(iconContainer.Background));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Buttons_use_the_supplied_labels_and_invoke_each_callback_once()
    {
        var failure = RunOnSta(() =>
        {
            var closeCalls = 0;
            var actionCalls = 0;
            var visual = ResultStateCardVisualFactory.Create(
                Options(
                    close: () => closeCalls++,
                    action: new ResultStateCardAction("Retry now", () => actionCalls++)),
                PluginPalette.For(lightTheme: false).ResultStateCard);

            Assert.Equal("Close card", visual.CloseButton.ToolTip);
            Assert.Equal("Close card", AutomationProperties.GetName(visual.CloseButton));
            Assert.Equal(12, Assert.IsType<System.Windows.Shapes.Path>(visual.CloseButton.Content).Width);
            var action = Assert.IsType<Button>(visual.PrimaryActionButton);
            Assert.Equal("Retry now", action.Content);
            Assert.Equal("Retry now", AutomationProperties.GetName(action));

            visual.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, closeCalls);
            Assert.Equal(1, actionCalls);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Missing_primary_action_does_not_create_or_reserve_an_action_row()
    {
        var failure = RunOnSta(() =>
        {
            var visual = ResultStateCardVisualFactory.Create(
                Options(action: null),
                PluginPalette.For(lightTheme: true).ResultStateCard);
            var root = Assert.IsType<Grid>(visual.Card.Child);

            Assert.Null(visual.PrimaryActionButton);
            Assert.Single(root.RowDefinitions);
            Assert.Equal(2, root.Children.Count);
            Assert.Single(root.Children.OfType<Button>());
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Invalid_required_options_are_rejected()
    {
        var failure = RunOnSta(() =>
        {
            var palette = PluginPalette.For(lightTheme: false).ResultStateCard;

            Assert.Throws<ArgumentNullException>(() => ResultStateCardVisualFactory.Create(null!, palette));
            Assert.Throws<ArgumentNullException>(() => ResultStateCardVisualFactory.Create(Options() with { Icon = null! }, palette));
            Assert.Throws<ArgumentException>(() => ResultStateCardVisualFactory.Create(Options() with { Message = " " }, palette));
            Assert.Throws<ArgumentException>(() => ResultStateCardVisualFactory.Create(Options() with { AccessibleName = "" }, palette));
            Assert.Throws<ArgumentException>(() => ResultStateCardVisualFactory.Create(Options() with { CloseLabel = "\t" }, palette));
            Assert.Throws<ArgumentNullException>(() => ResultStateCardVisualFactory.Create(Options() with { Close = null! }, palette));
            Assert.Throws<ArgumentException>(() => ResultStateCardVisualFactory.Create(
                Options(action: new ResultStateCardAction(" ", () => { })),
                palette));
            Assert.Throws<ArgumentNullException>(() => ResultStateCardVisualFactory.Create(
                Options(action: new ResultStateCardAction("Retry", null!)),
                palette));
        });

        Assert.Null(failure);
    }

    private static ResultStateCardOptions Options(
        Action? close = null,
        ResultStateCardAction? action = null) => new(
        OverlayVisualResources.CloseIconGeometry,
        "Message",
        "Accessible card",
        "Close card",
        close ?? (() => { }),
        action);

    private static Color BrushColor(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;

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
        thread.Join(TimeSpan.FromSeconds(20));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}
