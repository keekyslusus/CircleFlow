using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Settings;
using CircleToSearch.Shell.Onboarding;
using CircleToSearch.Ui;
using Xunit;
using static CircleToSearch.Tests.WpfUi;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class OnboardingTests
{
    [Fact]
    public void The_wizard_opens_once_and_every_close_completes_it() => OnSta(() =>
    {
        using var harness = new Harness();
        using var controller = harness.CreateController();
        controller.ShowIfNeeded();
        var window = Assert.IsAssignableFrom<Window>(controller.CurrentWindow);
        controller.ShowIfNeeded();
        Assert.Same(window, controller.CurrentWindow);
        Assert.False(harness.Settings.Snapshot.OnboardingCompleted);

        window.Close();
        Assert.Null(controller.CurrentWindow);
        Assert.True(harness.Settings.Snapshot.OnboardingCompleted);
        controller.ShowIfNeeded();
        Assert.Null(controller.CurrentWindow);
    });

    [Fact]
    public void Shutting_the_app_down_leaves_the_wizard_pending() => OnSta(() =>
    {
        using var harness = new Harness();
        var controller = harness.CreateController();
        controller.ShowIfNeeded();
        var window = controller.CurrentWindow!;
        controller.Dispose();
        Assert.False(window.IsVisible);
        Assert.False(harness.Settings.Snapshot.OnboardingCompleted);
    });

    [Fact]
    public void Existing_users_never_see_the_wizard() => OnSta(() =>
    {
        using var harness = new Harness(new AppSettings { OnboardingCompleted = true });
        using var controller = harness.CreateController();
        controller.ShowIfNeeded();
        Assert.Null(controller.CurrentWindow);
    });

    [Fact]
    public void Developer_settings_can_reopen_a_completed_wizard_without_duplicating_it() => OnSta(() =>
    {
        using var harness = new Harness(new AppSettings { OnboardingCompleted = true });
        using var controller = harness.CreateController();
        controller.Show();
        var window = Assert.IsAssignableFrom<Window>(controller.CurrentWindow);
        controller.Show();
        Assert.Same(window, controller.CurrentWindow);
        window.Close();
        Assert.Null(controller.CurrentWindow);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Steps_switch_buttons_and_dots_without_resizing_the_window(bool light) => OnSta(() =>
    {
        using var harness = new Harness();
        var window = harness.CreateView(light).Window;
        var bindingErrors = new StringWriter();
        using var listener = new TextWriterTraceListener(bindingErrors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            window.Show();
            Pump();
            var size = new Size(window.ActualWidth, window.ActualHeight);
            AssertStep(window, 0, "skip", "next");
            Preview(window, $"onboarding-{Theme(light)}-1.png");
            Click(window, "next");
            AssertStep(window, 1, "back", "next");
            Preview(window, $"onboarding-{Theme(light)}-2.png");
            Click(window, "back");
            AssertStep(window, 0, "skip", "next");
            Click(window, "next");
            Click(window, "next");
            AssertStep(window, 2, "try");
            Assert.True(Find<CheckBox>(window, "Launch").IsVisible);
            var launch = Find<CheckBox>(window, "Launch");
            var tryIt = LogicalChildren(window).OfType<Button>().Single(button => Equals(button.Tag, "try"));
            Assert.True(tryIt.TranslatePoint(new Point(), launch).X - launch.ActualWidth >= 8, "The checkbox touches Try it.");
            Preview(window, $"onboarding-{Theme(light)}-3.png");
            Pump();
            Assert.Equal(size, new Size(window.ActualWidth, window.ActualHeight));
            listener.Flush();
            Assert.Equal(string.Empty, bindingErrors.ToString());
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            window.Close();
        }
    });

    [Fact]
    public void Russian_text_fits_every_step() => OnSta(() =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Languages");
        var russian = LocalUiStrings.LoadEnglish(directory).Translate(directory, CultureInfo.GetCultureInfo("ru"));
        using var harness = new Harness(strings: new UiStrings(russian.Get));
        var window = harness.CreateView(light: false).Window;
        try
        {
            window.Show();
            Pump();
            var root = (FrameworkElement)window.Content;
            for (var step = 0; step < 3; step++)
            {
                Assert.All(VisualChildren(root).OfType<TextBlock>().Where(text => text.IsVisible && text.Text.Length > 0), text =>
                {
                    var bounds = text.TransformToAncestor(root).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                    Assert.True(bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1,
                        $"step {step + 1}: '{text.Text}' overflows");
                    Assert.True(text.ActualWidth + text.Margin.Left + text.Margin.Right >= text.DesiredSize.Width - 1,
                        $"step {step + 1}: '{text.Text}' is clipped");
                });
                Preview(window, $"onboarding-ru-{step + 1}.png");
                if (step < 2) Click(window, "next");
                Pump();
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Try_it_applies_the_startup_choice_and_opens_the_overlay_and_skip_does_neither(bool launch) => OnSta(() =>
    {
        using var harness = new Harness();
        using var controller = harness.CreateController();
        controller.ShowIfNeeded();
        var window = controller.CurrentWindow!;
        Click(window, "next");
        Click(window, "next");
        Find<CheckBox>(window, "Launch").IsChecked = launch;
        Click(window, "try");
        Assert.Null(controller.CurrentWindow);
        Assert.True(harness.Settings.Snapshot.OnboardingCompleted);
        Assert.Equal(launch, harness.Startup.Registration.IsEnabled);
        Assert.Equal([true], harness.OverlayOpens);

        using var denied = new Harness();
        using var deniedController = denied.CreateController();
        deniedController.ShowIfNeeded();
        var deniedWindow = deniedController.CurrentWindow!;
        Click(deniedWindow, "step:2");
        using (denied.Startup.DenyWrites()) Click(deniedWindow, "try");
        Assert.Same(deniedWindow, deniedController.CurrentWindow);
        Assert.False(denied.Settings.Snapshot.OnboardingCompleted);
        Assert.Empty(denied.OverlayOpens);
        Assert.True(Find<TextBlock>(deniedWindow, "LaunchFailed").IsVisible);
        Assert.False(Find<TextBlock>(deniedWindow, "Step3Text").IsVisible);
        Find<CheckBox>(deniedWindow, "Launch").IsChecked = false;
        Time.Advance(400);
        Assert.False(Find<TextBlock>(deniedWindow, "LaunchFailed").IsVisible);
        Assert.True(Find<TextBlock>(deniedWindow, "Step3Text").IsVisible);
        Click(deniedWindow, "try");
        Assert.Null(deniedController.CurrentWindow);
        Assert.Equal([true], denied.OverlayOpens);

        using var skipped = new Harness();
        using var skippedController = skipped.CreateController();
        skippedController.ShowIfNeeded();
        Click(skippedController.CurrentWindow!, "skip");
        Assert.Null(skippedController.CurrentWindow);
        Assert.True(skipped.Settings.Snapshot.OnboardingCompleted);
        Assert.Null(skipped.Startup.RunValue);
        Assert.Empty(skipped.OverlayOpens);
    });

    [Fact]
    public void Changing_the_shortcut_records_in_place_and_reports_problems() => OnSta(() =>
    {
        using var harness = new Harness(hotkeyAvailable: false);
        var window = harness.CreateView(light: false, statusLifetime: TimeSpan.FromMilliseconds(150)).Window;
        try
        {
            window.Show();
            Pump();
            var status = Find<TextBlock>(window, "HotkeyStatus");
            Assert.True(status.IsVisible);
            Assert.Equal(TestUiStrings.English.SettingsShortcutUnavailable("Ctrl+Alt+Space"), status.Text);
            Assert.Equal(["Ctrl", "Alt", "Space"], KeyLabels(window));
            var change = LogicalChildren(window).OfType<Button>().Single(button => Equals(button.Tag, "change-shortcut"));
            Assert.True(status.TranslatePoint(new Point(), change).Y - change.ActualHeight >= 12 - 2 * LayoutRoundingTolerance(window),
                "The status sits too close to the button.");

            var width = change.ActualWidth;
            Click(window, "change-shortcut");
            Assert.True(Find<TextBlock>(window, "RecordingPrompt").IsVisible);
            Assert.False(Find<ItemsControl>(window, "HotkeyKeys").IsVisible);
            Assert.True(Find<TextBlock>(window, "CancelShortcutContent").IsVisible);
            Assert.False(Find<StackPanel>(window, "ChangeShortcutContent").IsVisible);
            Assert.Equal(width, change.ActualWidth);
            Assert.False(status.IsVisible);
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.K)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            });
            if (UiAnimationPolicy.Enabled)
            {
                Time.Advance(60);
                Assert.InRange(status.Opacity, 0.01, 0.99);
                Assert.InRange(((ScaleTransform)status.RenderTransform).ScaleX, 0.92, 0.9999);
            }
            Time.Advance(400);
            Assert.True(status.IsVisible);
            Assert.Equal(TestUiStrings.English.SettingsShortcutInvalid, status.Text);
            Assert.True(Find<TextBlock>(window, "RecordingPrompt").IsVisible);
            Preview(window, "onboarding-dark-recording.png");
            Assert.True(Time.AdvanceUntil(() => !status.IsVisible), "The invalid-shortcut message did not expire.");
            Assert.True(Find<TextBlock>(window, "RecordingPrompt").IsVisible);

            if (UiAnimationPolicy.Enabled)
            {
                // Cancelling fades back the same way recording faded in.
                Raise(window, "change-shortcut");
                Time.Advance(60);
                Assert.InRange(Find<ItemsControl>(window, "HotkeyKeys").Opacity, 0.01, 0.99);
                Assert.InRange(Find<TextBlock>(window, "RecordingPrompt").Opacity, 0.01, 0.99);
                Assert.InRange(Find<StackPanel>(window, "ChangeShortcutContent").Opacity, 0.01, 0.99);
                Time.Advance(500);
                Assert.False(Find<TextBlock>(window, "RecordingPrompt").IsVisible);
                Click(window, "change-shortcut");
            }

            Press(window, Key.Escape);
            Assert.False(Find<TextBlock>(window, "RecordingPrompt").IsVisible);
            Assert.True(Find<ItemsControl>(window, "HotkeyKeys").IsVisible);
            Assert.True(Find<StackPanel>(window, "ChangeShortcutContent").IsVisible);
            Assert.False(Find<TextBlock>(window, "CancelShortcutContent").IsVisible);
            Assert.Equal(width, change.ActualWidth);
            Assert.Equal("Ctrl+Alt+Space", harness.Settings.Snapshot.HotkeyGesture);
            Assert.True(status.IsVisible);
            Assert.Equal(TestUiStrings.English.SettingsShortcutUnavailable("Ctrl+Alt+Space"), status.Text);

            // A message shown after the explanation expires; the explanation must outlive it.
            using var working = new Harness();
            var other = working.CreateView(light: false, statusLifetime: TimeSpan.FromMilliseconds(150)).Window;
            try
            {
                other.Show();
                Pump();
                Click(other, "change-shortcut");
                Press(other, Key.K);
                var otherStatus = Find<TextBlock>(other, "HotkeyStatus");
                Assert.True(otherStatus.IsVisible);
                Assert.True(Time.AdvanceUntil(() => !otherStatus.IsVisible), "The expiring message did not expire.");
                Assert.True(status.IsVisible);
            }
            finally { other.Close(); }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Steps_slide_in_the_direction_of_travel_and_dots_jump_to_any_step() => OnSta(() =>
    {
        using var harness = new Harness();
        var window = harness.CreateView(light: false).Window;
        try
        {
            window.Show();
            Pump();
            if (UiAnimationPolicy.Enabled)
            {
                Raise(window, "next");
                Time.Advance(120);
                Assert.True(Offset(window, "Step1Art") < 0 && Offset(window, "Step1Copy") < 0);
                Assert.True(Offset(window, "Step2Art") > 0 && Offset(window, "Step2Copy") > 0);
                Time.Advance(400);
                Assert.Equal(0, Offset(window, "Step2Art"));
                Click(window, "next");

                Raise(window, "step:0");
                Time.Advance(120);
                Assert.True(Offset(window, "Step3Art") > 0);
                Assert.True(Offset(window, "Step1Art") < 0);
                Assert.False(Find<FrameworkElement>(window, "Step2Art").IsVisible);
                // A second choice mid-slide turns the leaving step around instead of jumping.
                var leaving = Offset(window, "Step3Art");
                Raise(window, "step:2");
                Time.Advance(16);
                Assert.InRange(Offset(window, "Step3Art"), 0, leaving);
                Time.Advance(500);
            }
            if (UiAnimationPolicy.Enabled)
            {
                Click(window, "step:0");
                Raise(window, "next");
                Time.Advance(120);
                var leaving = Offset(window, "Step1Art");
                Raise(window, "next");
                Time.Advance(32);
                Assert.True(Find<FrameworkElement>(window, "Step1Art").IsVisible);
                Assert.True(Offset(window, "Step1Art") < leaving);
                Time.Advance(500);
                Assert.False(Find<FrameworkElement>(window, "Step1Art").IsVisible);
                AssertStep(window, 2, "try");
            }
            Click(window, "step:1");
            AssertStep(window, 1, "back", "next");
            Click(window, "step:2");
            AssertStep(window, 2, "try");
            Assert.All(Find<StackPanel>(window, "Steps").Children.OfType<Button>(), dot =>
                Assert.False(string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(dot))));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Rippling_controls_clip_the_ripple_to_their_own_rounded_corners() => OnSta(() =>
    {
        using var harness = new Harness();
        var window = harness.CreateView(light: false).Window;
        try
        {
            window.Show();
            Pump();
            var rippling = LogicalChildren(window).OfType<Control>().Where(Ui.Effects.ControlRippleHost.GetIsEnabled).ToArray();
            Assert.NotEmpty(rippling);
            Assert.All(rippling, control =>
            {
                control.ApplyTemplate();
                var chrome = Assert.IsType<Border>(control.Template.FindName("Chrome", control));
                Assert.Equal(PluginShapes.SmallCorners, chrome.CornerRadius);
            });
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("Ctrl+S", false)]
    [InlineData("Ctrl+Alt+Space", false)]
    [InlineData("Ctrl+Alt+Shift+K", false)]
    [InlineData("Ctrl+S", true)]
    public void The_shortcut_stays_centered_whatever_its_length(string gesture, bool russian) => OnSta(() =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Languages");
        var strings = russian
            ? new UiStrings(LocalUiStrings.LoadEnglish(directory).Translate(directory, CultureInfo.GetCultureInfo("ru")).Get)
            : null;
        using var harness = new Harness(new AppSettings { HotkeyGesture = gesture }, strings: strings);
        var window = harness.CreateView(light: false).Window;
        try
        {
            window.Show();
            Pump();
            var root = (FrameworkElement)window.Content;
            var keys = (Panel)VisualTreeHelper.GetParent(
                VisualChildren(Find<ItemsControl>(window, "HotkeyKeys")).OfType<ContentPresenter>().First());
            var left = keys.TranslatePoint(new Point(), root).X;
            Assert.InRange(left + keys.ActualWidth / 2, root.ActualWidth / 2 - 1, root.ActualWidth / 2 + 1);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_model_saves_a_new_shortcut_and_reports_a_taken_one()
    {
        using var harness = new Harness(applyHotkey: gesture => gesture == "Ctrl+Alt+K"
            ? new(false, new("Ctrl+Alt+Space", true))
            : new(true, new(gesture ?? string.Empty, gesture is not null)));
        Assert.True(harness.Model.HotkeyActive);
        Assert.Equal(TestUiStrings.English.SettingsShortcutUnavailable("Ctrl+Alt+K"), harness.Model.ChangeHotkey("Ctrl+Alt+K"));
        Assert.Equal("Ctrl+Alt+Space", harness.Model.HotkeyGesture);
        Assert.Equal(TestUiStrings.English.SettingsShortcutSaved, harness.Model.ChangeHotkey("Ctrl+Shift+1"));
        Assert.Equal("Ctrl+Shift+1", harness.Model.HotkeyGesture);
        Assert.True(harness.Model.HotkeyActive);
    }

    private static void AssertStep(Window window, int step, params string[] buttons)
    {
        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(index == step, Find<FrameworkElement>(window, $"Step{index + 1}Art").IsVisible);
            Assert.Equal(index == step, Find<FrameworkElement>(window, $"Step{index + 1}Copy").IsVisible);
            Assert.Equal(index == step ? 18 : 6, ((Border)((Button)Find<StackPanel>(window, "Steps").Children[index]).Content).ActualWidth,
                LayoutRoundingTolerance(window));
        }
        string[] footer = ["skip", "back", "next", "try"];
        var shown = LogicalChildren(window).OfType<Button>()
            .Where(button => button.IsVisible && button.Tag is string tag && footer.Contains(tag))
            .Select(button => (string)button.Tag).OrderBy(tag => tag).ToArray();
        Assert.Equal(buttons.OrderBy(tag => tag).ToArray(), shown);
        Assert.Same(FocusManager.GetFocusedElement(window), LogicalChildren(window).OfType<Button>().Single(button => Equals(button.Tag, buttons[^1])));
    }

    private static string[] KeyLabels(Window window) =>
        Find<ItemsControl>(window, "HotkeyKeys").Items.Cast<object>()
            .Select(item => (string)item.GetType().GetProperty("Label")!.GetValue(item)!).ToArray();

    private static string Theme(bool light) => light ? "light" : "dark";

    private static T Find<T>(Window window, string name) where T : FrameworkElement => (T)window.FindName(name);

    [ThreadStatic] private static ManualAnimationClock? _time;

    private static ManualAnimationClock Time => _time ?? throw new InvalidOperationException("OnSta installs the clock.");

    private static double Offset(Window window, string name) =>
        ((TranslateTransform)Find<FrameworkElement>(window, name).RenderTransform).X;

    // Clicks and key presses run every transition to its end, so assertions see the settled state.
    private static void Click(Window window, string tag)
    {
        Raise(window, tag);
        Time.Advance(520);
    }

    private static void Raise(Window window, string tag)
    {
        var button = LogicalChildren(window).OfType<Button>().Single(button => Equals(button.Tag, tag));
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static void Press(Window window, Key key)
    {
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
        Time.Advance(520);
    }

    // Set CTS_ONBOARDING_PREVIEW=1 to compare the rendered steps with the design in tests/temp.
    private static void Preview(Window window, string filename)
    {
        if (Environment.GetEnvironmentVariable("CTS_ONBOARDING_PREVIEW") != "1") return;
        Pump();
        SavePng((FrameworkElement)window.Content, filename);
    }

    private static void OnSta(Action action) => WpfUi.OnSta(time =>
    {
        _time = time;
        action();
    });

    private sealed class Harness : IDisposable
    {
        public Harness(AppSettings? initial = null, bool hotkeyAvailable = true, UiStrings? strings = null,
            Func<string?, Trigger.HotkeyApplyResult>? applyHotkey = null)
        {
            Settings = TestSettings.Create(initial, applyHotkey: applyHotkey ?? (gesture =>
                new(hotkeyAvailable, new(gesture ?? string.Empty, hotkeyAvailable && gesture is not null))));
            Settings.InitializeHotkey();
            Strings = strings ?? TestUiStrings.English;
            // Completion is saved when the window closes, so it tells whether the overlay opened after the wizard was gone.
            Model = new OnboardingModel(Settings, Startup.Registration, Strings,
                () => OverlayOpens.Add(Settings.Snapshot.OnboardingCompleted));
        }

        public List<bool> OverlayOpens { get; } = [];

        public SettingsService Settings { get; }
        public TestStartupRegistry Startup { get; } = new();
        public UiStrings Strings { get; }
        public OnboardingModel Model { get; }

        public OnboardingWindowView CreateView(bool light, TimeSpan? statusLifetime = null) =>
            new(Strings, light, new AppPaths().TrayIconPath, Model, statusLifetime);

        public OnboardingWindowController CreateController() =>
            new(Dispatcher.CurrentDispatcher, Model, () => CreateView(light: false).Window);

        public void Dispose() => Startup.Dispose();
    }
}
