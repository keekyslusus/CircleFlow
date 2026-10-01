using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class StateCardVisualFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_builds_the_state_card_with_the_selected_palette(bool lightTheme)
    {
        var failure = RunOnSta(() =>
        {
            var palette = PluginPalette.For(lightTheme).StateCard;
            var geometry = Geometry.Parse("M0 0 10 0 10 10Z");
            var visual = StateCardVisualFactory.Create(
                new StateCardOptions(
                    geometry,
                    "Result message",
                    "Result state",
                    "Close result",
                    () => { },
                    new StateCardAction("Try again", () => { })),
                palette);

            Assert.Equal(palette.Surface, BrushColor(visual.Card.Background));
            Assert.Equal(palette.Border, BrushColor(visual.Card.BorderBrush));
            Assert.Equal("Result state", AutomationProperties.GetName(visual.Card));
            var shadow = Assert.IsType<DropShadowEffect>(visual.Card.Effect);
            Assert.Equal(PluginPalette.OpaqueBlack, shadow.Color);
            Assert.Equal(palette.ShadowOpacity, shadow.Opacity);

            Assert.Same(geometry, visual.Icon.Data);
            Assert.Equal(palette.OnSecondaryContainer, BrushColor(visual.Icon.Fill));
            Assert.Equal("Result message", visual.Message.Text);
            Assert.Equal(palette.Text, BrushColor(visual.Message.Foreground));
            Assert.Null(visual.Title);

            var root = Assert.IsType<Grid>(visual.Card.Child);
            Assert.Equal(2, root.RowDefinitions.Count);
            Assert.Equal(3, root.Children.Count);
            var messageRow = Assert.IsType<StackPanel>(root.Children[1]);
            var iconContainer = Assert.IsType<Border>(messageRow.Children[0]);
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
            var visual = StateCardVisualFactory.Create(
                Options(
                    close: () => closeCalls++,
                    action: new StateCardAction("Retry now", () => actionCalls++)),
                PluginPalette.For(lightTheme: false).StateCard);

            Assert.Equal("Close card", visual.CloseButton.ToolTip);
            Assert.Equal("Close card", AutomationProperties.GetName(visual.CloseButton));
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
            var visual = StateCardVisualFactory.Create(
                Options(action: null),
                PluginPalette.For(lightTheme: true).StateCard);
            var root = Assert.IsType<Grid>(visual.Card.Child);

            Assert.Null(visual.PrimaryActionButton);
            Assert.Null(visual.Title);
            Assert.Single(root.RowDefinitions);
            Assert.Equal(2, root.Children.Count);
            Assert.Single(root.Children.OfType<Button>());
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Optional_title_uses_the_card_palette_and_wraps_above_the_message()
    {
        var failure = RunOnSta(() =>
        {
            var palette = PluginPalette.For(lightTheme: true).StateCard;
            var visual = StateCardVisualFactory.Create(Options() with { Title = "Privacy consent" }, palette);
            var row = Assert.IsType<StackPanel>(Assert.IsType<Grid>(visual.Card.Child).Children[1]);
            var column = Assert.IsType<StackPanel>(row.Children[1]);
            var iconContainer = Assert.IsType<Border>(row.Children[0]);
            var title = Assert.IsType<TextBlock>(visual.Title);

            Assert.Equal(VerticalAlignment.Top, iconContainer.VerticalAlignment);
            Assert.Same(title, column.Children[0]);
            Assert.Same(visual.Message, column.Children[1]);
            Assert.Equal("Privacy consent", title.Text);
            Assert.Equal(FontWeights.SemiBold, title.FontWeight);
            Assert.Equal(OverlayVisualResources.Font, title.FontFamily);
            Assert.Equal(palette.Text, BrushColor(title.Foreground));
            Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
            Assert.Equal(TextWrapping.Wrap, visual.Message.TextWrapping);
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Titled_card_aligns_the_icon_with_the_title_even_for_a_long_message()
    {
        var failure = RunOnSta(() =>
        {
            var visual = StateCardVisualFactory.Create(Options() with
            {
                Title = "Translate this screenshot?",
                Message = string.Join(' ', Enumerable.Repeat("Personal information will be shared.", 12)),
            }, PluginPalette.For(lightTheme: false).StateCard);
            visual.Card.Measure(new Size(340, double.PositiveInfinity));
            visual.Card.Arrange(new Rect(0, 0, 340, visual.Card.DesiredSize.Height));

            var row = Assert.IsType<StackPanel>(Assert.IsType<Grid>(visual.Card.Child).Children[1]);
            var iconContainer = Assert.IsType<Border>(row.Children[0]);
            var iconTop = iconContainer.TransformToAncestor(visual.Card).Transform(new Point()).Y;
            var titleTop = Assert.IsType<TextBlock>(visual.Title)
                .TransformToAncestor(visual.Card).Transform(new Point()).Y;
            Assert.InRange(Math.Abs(iconTop - titleTop), 0, 4);
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Constrained_titled_card_scrolls_its_text_without_resizing_the_action_row()
    {
        var failure = RunOnSta(() =>
        {
            var visual = StateCardVisualFactory.Create(
                Options(action: new StateCardAction("Continue", () => { })) with
                {
                    Title = "Consent",
                    CardWidth = 304,
                    CardMaxHeight = 120,
                }, PluginPalette.For(lightTheme: false).StateCard);
            var root = Assert.IsType<Grid>(visual.Card.Child);
            var row = Assert.IsType<StackPanel>(root.Children[1]);
            Assert.Equal(VerticalAlignment.Top, Assert.IsType<Border>(row.Children[0]).VerticalAlignment);
            var scroll = Assert.IsType<ScrollViewer>(row.Children[1]);

            Assert.Equal(304, visual.Card.Width);
            Assert.Equal(120, visual.Card.MaxHeight);
            Assert.Equal(194, scroll.Width);
            Assert.Equal(50, scroll.MaxHeight);
            Assert.Same(visual.Title, Assert.IsType<StackPanel>(scroll.Content).Children[0]);
            Assert.Same(visual.Message, Assert.IsType<StackPanel>(scroll.Content).Children[1]);
            Assert.Same(visual.PrimaryActionButton, root.Children[2]);
        });
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(320)]
    public void Constrained_title_and_message_share_the_compact_card_text_edge(double maxHeight)
    {
        var failure = RunOnSta(() =>
        {
            var palette = PluginPalette.For(lightTheme: false).StateCard;
            var message = string.Join(' ', Enumerable.Repeat("Personal information will be shared.", 12));
            var compact = StateCardVisualFactory.Create(Options() with { Message = message }, palette);
            var consent = StateCardVisualFactory.Create(Options(action: new StateCardAction("Continue", () => { })) with
            {
                Message = message,
                Title = "Translate this screenshot?",
                CardMaxHeight = maxHeight,
            }, palette);
            foreach (var card in new[] { compact.Card, consent.Card })
            {
                card.Measure(new Size(340, double.PositiveInfinity));
                card.Arrange(new Rect(0, 0, 340, card.DesiredSize.Height));
            }

            var compactX = compact.Message.TransformToAncestor(compact.Card).Transform(new Point()).X;
            var messageX = consent.Message.TransformToAncestor(consent.Card).Transform(new Point()).X;
            var titleX = Assert.IsType<TextBlock>(consent.Title)
                .TransformToAncestor(consent.Card).Transform(new Point()).X;
            Assert.Equal(compactX, messageX);
            Assert.Equal(compactX, titleX);
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Invalid_required_options_are_rejected()
    {
        var failure = RunOnSta(() =>
        {
            var palette = PluginPalette.For(lightTheme: false).StateCard;

            Assert.Throws<ArgumentException>(() => StateCardVisualFactory.Create(Options() with { Message = " " }, palette));
            Assert.Throws<ArgumentException>(() => StateCardVisualFactory.Create(Options() with { AccessibleName = "" }, palette));
            Assert.Throws<ArgumentException>(() => StateCardVisualFactory.Create(Options() with { CloseLabel = "\t" }, palette));
            Assert.Throws<ArgumentException>(() => StateCardVisualFactory.Create(Options() with { Title = " " }, palette));
            Assert.Throws<ArgumentOutOfRangeException>(() => StateCardVisualFactory.Create(Options() with { CardWidth = 100 }, palette));
            Assert.Throws<ArgumentOutOfRangeException>(() => StateCardVisualFactory.Create(Options() with { CardMaxHeight = 100 }, palette));
            Assert.Throws<ArgumentException>(() => StateCardVisualFactory.Create(
                Options(action: new StateCardAction(" ", () => { })),
                palette));
        });

        Assert.Null(failure);
    }

    private static StateCardOptions Options(
        Action? close = null,
        StateCardAction? action = null) => new(
        PluginIcons.CloseFilled,
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
