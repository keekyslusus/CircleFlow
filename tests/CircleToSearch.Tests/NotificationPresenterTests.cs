using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shape = System.Windows.Shapes.Path;
using System.Windows.Threading;
using CircleToSearch.Shell.Notifications;
using CircleToSearch.Ui;
using Xunit;
using static CircleToSearch.Tests.WpfUi;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class NotificationPresenterTests
{
    private static UiStrings Strings => TestUiStrings.English;

    [Fact]
    public void Notifications_marshal_to_dispatcher_run_the_action_once_and_ignore_work_after_disposal() => OnSta(time =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var presenter = Create();
        var calls = 0;
        Task.Run(() => presenter.ShowMessageWithButton(Strings.PluginTitle, "recognized track", "Open in Shazam", () =>
        {
            dispatcher.VerifyAccess();
            calls++;
            throw new InvalidOperationException("failed action");
        })).GetAwaiter().GetResult();
        Pump();
        var card = Assert.Single(presenter.Cards);
        Assert.True(presenter.Window!.IsVisible);
        Assert.Equal("recognized track", card.MessageText.Text);
        Assert.Equal("Open in Shazam", card.ActionButton!.Content);
        Click(card.ActionButton);
        Click(card.ActionButton);
        Assert.Equal(1, calls);
        Assert.Empty(presenter.Cards);
        time.Advance(300);
        Assert.Null(presenter.Window);

        presenter.ShowError(Strings.PluginTitle, "failure");
        Pump();
        Assert.Single(presenter.Cards);
        presenter.ShowMessage(Strings.PluginTitle, "queued before exit");
        presenter.Dispose();
        presenter.ShowMessage(Strings.PluginTitle, "late callback");
        Pump();
        Assert.Empty(presenter.Cards);
        Assert.Null(presenter.Window);
    });

    [Fact]
    public void Cards_show_the_app_header_a_distinct_title_an_error_mark_and_actions_only_when_given() => OnSta(time =>
    {
        using var presenter = Create();
        presenter.ShowMessage(Strings.PluginTitle, "plain");
        presenter.ShowError(Strings.UpdateFailedTitle, Strings.UpdateFailed);
        presenter.ShowMessageWithButton(Strings.UpdateAvailableTitle, "offer", Strings.UpdateInstall, () => { });
        Pump();
        var (plain, error, offer) = (presenter.Cards[0], presenter.Cards[1], presenter.Cards[2]);

        Assert.Null(plain.TitleText);
        Assert.Null(plain.ActionButton);
        Assert.Contains(Strings.PluginTitle, Texts(plain.Card));
        Assert.DoesNotContain(PluginIcons.ErrorOutlined, Icons(plain.Card));
        Assert.Equal(Strings.UpdateFailedTitle, error.TitleText!.Text);
        Assert.Contains(PluginIcons.ErrorOutlined, Icons(error.Card));
        Assert.Null(error.ActionButton);
        Assert.Equal(Strings.UpdateInstall, offer.ActionButton!.Content);
        Assert.Contains(Strings.Close, Texts(offer.Card));
        Assert.Equal(Strings.Close, AutomationName(offer.CloseButton));
    });

    [Fact]
    public void The_column_sits_on_the_right_edge_of_the_work_area_without_taking_focus() => OnSta(time =>
    {
        using var presenter = Create();
        presenter.ShowMessage(Strings.PluginTitle, "placement");
        Pump();
        var window = presenter.Window!;
        var work = SystemParameters.WorkArea;
        Assert.Equal(work.Right, window.Left + window.Width, 1);
        Assert.Equal(work.Top, window.Top, 1);
        Assert.Equal(work.Height, window.Height, 1);
        Assert.True(window.AllowsTransparency);
        Assert.True(window.Topmost);
        Assert.False(window.ShowActivated);
        Assert.False(window.ShowInTaskbar);
        Assert.False(window.IsActive);
    });

    [Fact]
    public void Cards_slide_in_from_the_right_and_out_again_and_the_empty_column_closes() => OnSta(time =>
    {
        using var presenter = Create();
        presenter.ShowMessage(Strings.PluginTitle, "first");
        presenter.ShowError(Strings.PluginTitle, "second");
        Pump();
        var (first, second) = (presenter.Cards[0], presenter.Cards[1]);
        var slide = (TranslateTransform)second.Card.RenderTransform;
        if (UiAnimationPolicy.Enabled)
        {
            time.Advance(60);
            Assert.InRange(second.Card.Opacity, 0.001, 0.999);
            Assert.InRange(slide.X, 0.001, 23.999);
            Assert.InRange(second.Root.Height, 0.001, second.Card.ActualHeight + NotificationCard.Gap - 0.001);
        }
        time.Advance(300);
        Assert.Equal(1, second.Card.Opacity);
        Assert.Equal(0, slide.X);
        Assert.Equal(second.Card.ActualHeight + NotificationCard.Gap, second.Root.Height, 1);

        Click(first.CloseButton);
        Assert.Equal([second], presenter.Cards);
        if (UiAnimationPolicy.Enabled)
        {
            time.Advance(60);
            Assert.Contains(first.Root, Stack(presenter).Children.Cast<UIElement>());
            Assert.InRange(first.Card.Opacity, 0.001, 0.999);
            Assert.False(first.Root.IsHitTestVisible);
        }
        time.Advance(300);
        Assert.DoesNotContain(first.Root, Stack(presenter).Children.Cast<UIElement>());

        Click(second.CloseButton);
        time.Advance(300);
        Assert.Null(presenter.Window);
    });

    [Fact]
    public void Plain_messages_dismiss_themselves_unless_hovered_while_errors_and_offers_stay() => OnSta(time =>
    {
        using var presenter = Create(TimeSpan.FromMilliseconds(80));
        presenter.ShowError(Strings.PluginTitle, "error");
        presenter.ShowMessageWithButton(Strings.PluginTitle, "offer", "Open", () => { });
        presenter.ShowMessage(Strings.PluginTitle, "hovered");
        Pump();
        var hovered = presenter.Cards[2];
        hovered.Card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        DispatcherPump.For(300);
        Assert.Equal(3, presenter.Cards.Count);

        hovered.Card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.True(DispatcherPump.Until(() => presenter.Cards.Count == 2));
        presenter.ShowMessage(Strings.PluginTitle, "information");
        Pump();
        Assert.Equal(3, presenter.Cards.Count);
        Assert.True(DispatcherPump.Until(() => presenter.Cards.Count == 2));
        Assert.Equal(["error", "offer"], presenter.Cards.Select(card => card.MessageText.Text));
    });

    [Fact]
    public void A_hovered_card_that_closes_does_not_start_its_timer_again() => OnSta(time =>
    {
        using var presenter = Create();
        presenter.ShowMessage(Strings.PluginTitle, "closed with the X");
        presenter.ShowMessage(Strings.PluginTitle, "closed with everything");
        Pump();
        var (closed, cleared) = (presenter.Cards[0], presenter.Cards[1]);
        Assert.True(closed.IsCountingDown);
        Hover(closed, Mouse.MouseEnterEvent);
        Assert.False(closed.IsCountingDown);
        Click(closed.CloseButton);
        Hover(closed, Mouse.MouseLeaveEvent);
        Assert.False(closed.IsCountingDown);

        Hover(cleared, Mouse.MouseEnterEvent);
        presenter.CloseAll();
        Hover(cleared, Mouse.MouseLeaveEvent);
        Assert.False(cleared.IsCountingDown);
    });

    [Fact]
    public void Long_messages_are_cut_off_with_the_full_text_in_a_tooltip() => OnSta(time =>
    {
        using var presenter = Create();
        var message = string.Join(" ", Enumerable.Repeat("The image could not be saved because the disk is full.", 40));
        presenter.ShowError(Strings.PluginTitle, message);
        Pump();
        time.Advance(300);
        var card = Assert.Single(presenter.Cards);
        Assert.InRange(card.MessageText.ActualHeight, 1, 240);
        Assert.Equal(TextTrimming.CharacterEllipsis, card.MessageText.TextTrimming);
        Assert.Equal(message, card.MessageText.ToolTip);
        Assert.True(card.Card.ActualHeight < presenter.Window!.ActualHeight / 2);
    });

    [Fact]
    public void A_repeated_offer_or_error_still_open_is_not_stacked_again() => OnSta(time =>
    {
        using var presenter = Create();
        var clicked = 0;
        presenter.ShowMessageWithButton(Strings.UpdateAvailableTitle, "offer", Strings.UpdateInstall, () => clicked++);
        presenter.ShowError(Strings.PluginTitle, "failure");
        presenter.ShowMessage(Strings.PluginTitle, "information");
        presenter.ShowMessageWithButton(Strings.UpdateAvailableTitle, "offer", Strings.UpdateInstall, () => { });
        presenter.ShowError(Strings.PluginTitle, "failure");
        presenter.ShowMessage(Strings.PluginTitle, "information");
        presenter.ShowMessageWithButton(Strings.UpdateAvailableTitle, "newer offer", Strings.UpdateInstall, () => { });
        Pump();
        Assert.Equal(["offer", "failure", "information", "information", "newer offer"],
            presenter.Cards.Select(card => card.MessageText.Text));

        Click(presenter.Cards[0].ActionButton!);
        Assert.Equal(1, clicked);
        presenter.ShowMessageWithButton(Strings.UpdateAvailableTitle, "offer", Strings.UpdateInstall, () => { });
        Pump();
        Assert.Equal("offer", presenter.Cards[^1].MessageText.Text);
    });

    [Fact]
    public void Closing_all_removes_every_card_at_once() => OnSta(time =>
    {
        using var presenter = Create();
        presenter.ShowMessage(Strings.PluginTitle, "one");
        presenter.ShowError(Strings.PluginTitle, "two");
        Pump();
        var window = presenter.Window!;
        presenter.CloseAll();
        Assert.Empty(presenter.Cards);
        Assert.Null(presenter.Window);
        Assert.False(window.IsVisible);
        presenter.ShowMessage(Strings.PluginTitle, "after");
        Pump();
        Assert.Single(presenter.Cards);
        Assert.NotSame(window, presenter.Window);
    });

    private static NotificationPresenter Create(TimeSpan? autoDismiss = null) => new(Dispatcher.CurrentDispatcher, Strings,
        new AppPaths(AppContext.BaseDirectory).TrayIconPath, SystemTheme.IsLight,
        new PluginLog(Path.Combine(TestOutputPaths.TempDirectory, "notifications-" + Guid.NewGuid().ToString("N"))),
        autoDismiss ?? TimeSpan.FromMinutes(5));

    private static StackPanel Stack(NotificationPresenter presenter) => (StackPanel)presenter.Window!.FindName("Cards");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    private static void Hover(NotificationCard card, RoutedEvent routedEvent) =>
        card.Card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = routedEvent });

    private static string? AutomationName(DependencyObject element) =>
        System.Windows.Automation.AutomationProperties.GetName(element);

    private static IEnumerable<string> Texts(DependencyObject root) =>
        Descendants(root).Select(element => element switch
        {
            TextBlock text => text.Text,
            ContentControl { Content: string content } => content,
            _ => null,
        }).OfType<string>();

    private static IEnumerable<Geometry> Icons(DependencyObject root) =>
        Descendants(root).OfType<Shape>().Select(path => path.Data);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
