using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Ui;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class ProviderMusicOverlayUiTests
{
    private static readonly SearchProviderDescriptor[] Providers =
    [
        new(SearchProviderIds.GoogleLens, "Google Lens"),
        new(SearchProviderIds.YandexImages, "Yandex Images"),
    ];

    [Fact]
    public void Action_tray_places_provider_between_selection_and_music_and_menu_excludes_current()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English,
                Providers,
                SearchProviderIds.GoogleLens);

            Assert.Same(visual.Chip, visual.ActionTray.Children[0]);
            Assert.Same(visual.ProviderButton, visual.ActionTray.Children[1]);
            Assert.Same(visual.MusicButton, visual.ActionTray.Children[2]);
            Assert.True(visual.Root.Children.IndexOf(visual.SelectionInputSurface) <
                        visual.Root.Children.IndexOf(visual.ActionUiRoot));
            Assert.False(visual.Screenshot.IsHitTestVisible);
            Assert.Equal(44, visual.ProviderButton!.Height);
            Assert.Equal(44, visual.MusicButton.Height);
            Assert.NotNull(visual.ProviderChevron);
            Assert.Equal(170, visual.Waveform.Width);
            Assert.Equal(40, visual.Waveform.Height);
            Assert.Equal(220, visual.ProviderMenu.Width);
            Assert.Equal(new CornerRadius(16), visual.ProviderMenu.CornerRadius);
            var item = Assert.Single(((StackPanel)visual.ProviderMenu.Child).Children.OfType<Button>());
            Assert.Equal(SearchProviderIds.YandexImages, item.Tag);
            Assert.Equal(36, item.MinHeight);
            Assert.Contains(
                "Yandex Images",
                Descendants((DependencyObject)item.Content).OfType<TextBlock>().Select(text => text.Text));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Provider_and_music_hover_keep_the_dock_surface_opaque()
    {
        foreach (var lightTheme in new[] { false, true })
        {
            var palette = PluginPalette.For(lightTheme);
            Assert.True(palette.Provider.Hover.A >= palette.Provider.Surface.A);
            Assert.True(palette.MusicButton.Hover.A >= palette.MusicButton.Surface.A);
            Assert.NotEqual(palette.Provider.Surface, palette.Provider.Hover);
            Assert.NotEqual(palette.MusicButton.Surface, palette.MusicButton.Hover);
            Assert.True(palette.Provider.Hover.R < palette.Provider.Surface.R);
            Assert.True(palette.MusicButton.Hover.R < palette.MusicButton.Surface.R);
        }
    }

    [Fact]
    public void Reopening_provider_menu_keeps_the_same_position()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                _ => { },
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            var provider = overlay.VisualState.ProviderButton!;
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var firstLeft = Canvas.GetLeft(overlay.VisualState.ProviderMenu);
            var firstTop = Canvas.GetTop(overlay.VisualState.ProviderMenu);
            Assert.InRange(overlay.VisualState.ProviderMenu.ActualHeight, 55, 57);
            var menuItem = Assert.Single(
                ((StackPanel)overlay.VisualState.ProviderMenu.Child).Children.OfType<Button>());
            var itemContent = Assert.IsAssignableFrom<FrameworkElement>(menuItem.Content);
            var contentLeft = itemContent.TranslatePoint(
                new Point(),
                overlay.VisualState.ProviderMenu).X;

            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();

            Assert.Equal(firstLeft, Canvas.GetLeft(overlay.VisualState.ProviderMenu));
            Assert.Equal(firstTop, Canvas.GetTop(overlay.VisualState.ProviderMenu));
            Assert.InRange(contentLeft, 18, 20);
            Assert.Equal(Visibility.Visible, overlay.VisualState.ProviderMenu.Visibility);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Action_buttons_are_separate_from_the_surface_that_captures_lasso_input()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                _ => { },
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            var provider = overlay.VisualState.ProviderButton!;
            provider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = provider,
            });

            Assert.NotSame(overlay.VisualState.SelectionInputSurface, Mouse.Captured);
            Assert.False(overlay.Dispatcher.HasShutdownStarted);

            overlay.VisualState.SelectionInputSurface.RaiseEvent(
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = overlay.VisualState.SelectionInputSurface,
                });

            Assert.Same(overlay.VisualState.SelectionInputSurface, Mouse.Captured);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Music_click_enters_listening_without_shutting_down_and_provider_remains_usable()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            var buttons = Descendants((DependencyObject)overlay.Content).OfType<Button>().ToArray();
            var music = Assert.Single(buttons, button =>
                AutomationProperties.GetName(button) == TestUiStrings.English.MusicRecognitionAction);
            music.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
            Assert.IsType<StartMusicRecognition>(Assert.Single(commands));
            Assert.False(overlay.Dispatcher.HasShutdownStarted);

            var provider = Assert.Single(buttons, button => ReferenceEquals(button.Tag, null) &&
                AutomationProperties.GetName(button).Contains("choose provider", StringComparison.Ordinal));
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var yandex = Descendants((DependencyObject)overlay.Content).OfType<Button>()
                .Single(button => Equals(button.Tag, SearchProviderIds.YandexImages));
            yandex.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(SearchProviderIds.YandexImages,
                Assert.IsType<ProviderSelected>(commands[1]).ProviderId);
            Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Real_pointer_click_on_shown_debug_and_music_buttons_publishes_commands_without_dispatcher_crash()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            overlay.Activate();
            Assert.True(NativeMethods.GetCursorPos(out var originalPointer));
            try
            {
                Assert.Equal(Visibility.Collapsed, overlay.VisualState.DebugPanel.Visibility);
                overlay.SetDebugPanelOpen(true);
                overlay.UpdateLayout();
                Assert.Equal(Visibility.Visible, overlay.VisualState.DebugPanel.Visibility);

                var noAudio = overlay.VisualState.DebugScenarioButtons.Children
                    .OfType<Button>()
                    .Single(button => Equals(button.Tag, MusicDebugScenario.NoAudio));
                ClickWithRealPointer(noAudio);
                PumpUntil(() => commands.Count >= 1);
                ClickWithRealPointer(overlay.VisualState.MusicButton);
                PumpUntil(() => commands.Count >= 2);

                Assert.Equal(
                    MusicDebugScenario.NoAudio,
                    Assert.IsType<MusicDebugScenarioSelected>(commands[0]).Scenario);
                Assert.IsType<StartMusicRecognition>(commands[1]);
                Assert.Equal(Visibility.Collapsed, overlay.VisualState.DebugPanel.Visibility);
                Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
            }
            finally
            {
                SetCursorPos(originalPointer.X, originalPointer.Y);
                overlay.CloseFromSession();
                Dispatcher.Run();
            }
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Match_result_stops_waveform_keeps_dim_and_exposes_actions()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            overlay.ShowListening();
            Assert.Equal(Visibility.Visible, overlay.VisualState.ListeningLayer.Visibility);
            Assert.Equal(1, overlay.VisualState.Dim.Opacity);
            if (OverlayVisualFactory.AnimationsEnabled())
                Assert.True(overlay.VisualState.Screenshot.HasAnimatedProperties);

            overlay.ShowMusicResult(MusicRecognitionOutcome.Matched(new ShazamRecognition(
                "Track", "Artist", null, null, null, null, "https://www.shazam.com/track/1")));
            overlay.UpdateLayout();

            Assert.Equal(OverlayInteractionMode.MusicResult, overlay.Mode);
            Assert.False(overlay.VisualState.Waveform.IsRendering);
            Assert.Equal(Visibility.Collapsed, overlay.VisualState.ListeningLayer.Visibility);
            Assert.Equal(Visibility.Visible, overlay.VisualState.ResultHost.Visibility);
            Assert.Equal(VerticalAlignment.Bottom, overlay.VisualState.ResultHost.VerticalAlignment);
            var card = Assert.Single(overlay.VisualState.ResultHost.Children.OfType<Border>());
            Assert.Equal(48, card.Height);
            Assert.Equal(new CornerRadius(24), card.CornerRadius);
            var actionNames = Descendants(overlay.VisualState.ResultHost).OfType<Button>()
                .Select(AutomationProperties.GetName)
                .ToArray();
            Assert.Contains(TestUiStrings.English.CopyTrackInfo, actionNames);
            Assert.Contains(TestUiStrings.English.OpenInShazam, actionNames);
            Assert.Contains(TestUiStrings.English.Close, actionNames);
            Assert.All(
                Descendants(overlay.VisualState.ResultHost).OfType<Button>(),
                button => Assert.Equal(30, button.Width));
            var close = Descendants(overlay.VisualState.ResultHost).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close);
            Assert.Equal(12, Assert.IsType<System.Windows.Shapes.Path>(close.Content).Width);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.IsType<DismissMusicResult>(Assert.Single(commands));
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            Assert.Equal(Visibility.Collapsed, overlay.VisualState.ResultHost.Visibility);
            Assert.False(overlay.Dispatcher.HasShutdownStarted);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Theory]
    [InlineData(MusicRecognitionStatus.NoMatch)]
    [InlineData(MusicRecognitionStatus.NoAudio)]
    [InlineData(MusicRecognitionStatus.RateLimited)]
    [InlineData(MusicRecognitionStatus.ServiceError)]
    [InlineData(MusicRecognitionStatus.DeviceError)]
    public void Every_music_state_close_action_dismisses_only_the_result(
        MusicRecognitionStatus status)
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English,
                Providers,
                SearchProviderIds.GoogleLens);
            var commands = new List<IOverlayCommand>();
            OverlayVisualFactory.PresentMusicResult(
                visual,
                MusicRecognitionOutcome.From(status),
                TestUiStrings.English,
                commands.Add,
                (_, _) => { });
            var close = Descendants(visual.ResultHost).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close);

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.IsType<DismissMusicResult>(Assert.Single(commands));
            Assert.Equal(12, Assert.IsType<System.Windows.Shapes.Path>(close.Content).Width);
        });

        Assert.Null(failure);
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
        thread.Join(TimeSpan.FromSeconds(20));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }

    private static void ClickWithRealPointer(FrameworkElement element)
    {
        element.UpdateLayout();
        var center = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        if (!SetCursorPos((int)Math.Round(center.X), (int)Math.Round(center.Y)))
            throw new InvalidOperationException("SetCursorPos failed.");
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            frame.Continue = false;
        };
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        poll.Tick += (_, _) =>
        {
            if (!condition()) return;
            poll.Stop();
            timeout.Stop();
            frame.Continue = false;
        };
        timeout.Start();
        poll.Start();
        Dispatcher.PushFrame(frame);
        poll.Stop();
        Assert.True(condition(), "The real pointer click was not delivered to the WPF button.");
    }

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
