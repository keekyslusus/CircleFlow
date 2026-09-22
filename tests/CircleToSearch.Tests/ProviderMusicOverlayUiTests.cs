using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
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

            Assert.Same(visual.Actions.Chip, visual.Actions.Tray.Children[0]);
            Assert.Same(visual.Provider!.Button, visual.Actions.Tray.Children[1]);
            Assert.Same(visual.TranslationAction.Button, visual.Actions.Tray.Children[2]);
            Assert.Same(visual.Music.Button, visual.Actions.Tray.Children[3]);
            Assert.True(visual.Root.Children.IndexOf(visual.Selection.InputSurface) <
                        visual.Root.Children.IndexOf(visual.Bottom.Root));
            Assert.False(visual.Selection.Screenshot.IsHitTestVisible);
            Assert.Equal(44, visual.Provider.Button.Height);
            Assert.Equal(44, visual.TranslationAction.Button.Width);
            Assert.Equal(44, visual.TranslationAction.Button.Height);
            Assert.Equal(new Thickness(), visual.TranslationAction.Button.Padding);
            Assert.Equal(HorizontalAlignment.Center, visual.TranslationAction.Button.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, visual.TranslationAction.Button.VerticalContentAlignment);
            Assert.Same(
                PluginIcons.TranslateFilled,
                visual.TranslationAction.Icon.Data);
            Assert.Equal(18, visual.TranslationAction.Icon.Width);
            Assert.Equal(18, visual.TranslationAction.Icon.Height);
            Assert.Equal(42, visual.TranslationAction.LoadingIndicator.Width);
            Assert.Equal(42, visual.TranslationAction.LoadingIndicator.Height);
            Assert.Equal(44, visual.Music.Button.Height);
            Assert.Equal(new Thickness(), visual.Music.Button.Padding);
            Assert.Equal(HorizontalAlignment.Center, visual.Music.Button.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, visual.Music.Button.VerticalContentAlignment);
            Assert.Equal(42, visual.Music.LoadingIndicator.Width);
            Assert.Equal(42, visual.Music.LoadingIndicator.Height);
            Assert.Equal(6, visual.Provider.Chevron.Width);
            Assert.Equal(6, visual.Provider.Chevron.Height);
            var chevronSlot = Assert.IsType<Grid>(visual.Provider.Chevron.Parent);
            Assert.Equal(14, chevronSlot.Width);
            Assert.Equal(14, chevronSlot.Height);
            Assert.Equal(170, visual.Music.Waveform.Width);
            Assert.Equal(40, visual.Music.Waveform.Height);
            MusicOverlayVisualPresenter.SetListeningState(visual.Music, listening: true, lightTheme: false);
            var listeningBackground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
                visual.Music.Button.Background).Color;
            Assert.True(listeningBackground.A >= PluginPalette.For(lightTheme: false).MusicButton.Surface.A);
            Assert.NotEqual(
                PluginPalette.WithAlpha(SystemAccentColor.Read(), 0.16),
                listeningBackground);
            MusicOverlayVisualPresenter.SetListeningState(visual.Music, listening: false, lightTheme: false);
            Assert.Equal(220, visual.Provider.Menu.Width);
            Assert.Equal(new CornerRadius(16), visual.Provider.Menu.CornerRadius);
            var item = Assert.Single(((StackPanel)visual.Provider.Menu.Child).Children.OfType<Button>());
            Assert.Equal(SearchProviderIds.YandexImages, item.Tag);
            Assert.Equal(36, item.MinHeight);
            Assert.Contains(
                "Yandex Images",
                Descendants((DependencyObject)item.Content).OfType<TextBlock>().Select(text => text.Text));
            visual.Music.LoadingIndicator.Dispose();
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
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            var provider = overlay.VisualState.Provider!.Button;
            var providerWidth = provider.ActualWidth;
            var chipLeft = overlay.VisualState.Actions.Chip.TranslatePoint(
                new Point(),
                overlay.VisualState.Actions.Tray).X;
            var musicLeft = overlay.VisualState.Music.Button.TranslatePoint(
                new Point(),
                overlay.VisualState.Actions.Tray).X;
            Assert.True(provider.Focus());
            overlay.UpdateLayout();
            var providerChrome = Assert.IsType<Border>(provider.Template.FindName("Chrome", provider));

            Assert.Equal(new Thickness(1), providerChrome.BorderThickness);
            Assert.Equal(providerWidth, provider.ActualWidth);
            Assert.Equal(chipLeft, overlay.VisualState.Actions.Chip.TranslatePoint(
                new Point(),
                overlay.VisualState.Actions.Tray).X);
            Assert.Equal(musicLeft, overlay.VisualState.Music.Button.TranslatePoint(
                new Point(),
                overlay.VisualState.Actions.Tray).X);
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var firstLeft = Canvas.GetLeft(overlay.VisualState.Provider.Menu);
            var firstTop = Canvas.GetTop(overlay.VisualState.Provider.Menu);
            Assert.InRange(overlay.VisualState.Provider.Menu.ActualHeight, 55, 57);
            var menuItem = Assert.Single(
                ((StackPanel)overlay.VisualState.Provider.Menu.Child).Children.OfType<Button>());
            var itemContent = Assert.IsAssignableFrom<FrameworkElement>(menuItem.Content);
            var contentLeft = itemContent.TranslatePoint(
                new Point(),
                overlay.VisualState.Provider.Menu).X;

            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            MusicOverlayVisualPresenter.PresentResult(
                overlay.VisualState.Music,
                MusicRecognitionOutcome.Matched(new ShazamRecognition(
                    "Track", "Artist", null, null, null, null, "https://www.shazam.com/track/1")),
                TestUiStrings.English,
                lightTheme: overlay.VisualState.LightTheme,
                _ => { },
                (_, _) => { });
            overlay.UpdateLayout();
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();

            Assert.Equal(firstLeft, Canvas.GetLeft(overlay.VisualState.Provider.Menu));
            Assert.Equal(firstTop, Canvas.GetTop(overlay.VisualState.Provider.Menu));
            Assert.InRange(
                Canvas.GetLeft(overlay.VisualState.Provider.Menu),
                0,
                overlay.VisualState.Bottom.Root.ActualWidth - overlay.VisualState.Provider.Menu.ActualWidth);
            Assert.InRange(
                Canvas.GetTop(overlay.VisualState.Provider.Menu),
                0,
                overlay.VisualState.Bottom.Root.ActualHeight - overlay.VisualState.Provider.Menu.ActualHeight);

            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.VisualState.Music.ResultHost.Visibility = Visibility.Collapsed;
            overlay.UpdateLayout();
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();

            Assert.Equal(firstLeft, Canvas.GetLeft(overlay.VisualState.Provider.Menu));
            Assert.Equal(firstTop, Canvas.GetTop(overlay.VisualState.Provider.Menu));
            Assert.InRange(contentLeft, 18, 20);
            Assert.Equal(Visibility.Visible, overlay.VisualState.Provider.Menu.Visibility);
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
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            var provider = overlay.VisualState.Provider!.Button;
            var result = MusicOverlayVisualPresenter.PresentResult(
                overlay.VisualState.Music,
                MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio),
                TestUiStrings.English,
                lightTheme: overlay.VisualState.LightTheme,
                _ => { },
                (_, _) => { });
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var menuCenter = overlay.VisualState.Provider.Menu.TransformToAncestor(overlay).Transform(
                new Point(
                    overlay.VisualState.Provider.Menu.ActualWidth / 2,
                    overlay.VisualState.Provider.Menu.ActualHeight / 2));
            var resultCenter = result.TransformToAncestor(overlay).Transform(
                new Point(result.ActualWidth / 2, result.ActualHeight / 2));

            Assert.True(overlay.IsActionTrayInteraction(null, menuCenter));
            Assert.True(overlay.IsActionTrayInteraction(null, resultCenter));
            Assert.False(overlay.IsActionTrayInteraction(null, new Point(10, 10)));
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var rippleLayer = AdornerLayer.GetAdornerLayer(provider);
            Assert.NotNull(rippleLayer);
            provider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = provider,
            });
            Assert.NotEmpty(rippleLayer.GetAdorners(provider)!);
            provider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = provider,
            });

            Assert.NotSame(overlay.VisualState.Selection.InputSurface, Mouse.Captured);
            Assert.False(overlay.Dispatcher.HasShutdownStarted);

            overlay.VisualState.Selection.InputSurface.RaiseEvent(
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = overlay.VisualState.Selection.InputSurface,
                });

            Assert.Same(overlay.VisualState.Selection.InputSurface, Mouse.Captured);
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
                TestOverlayControllers.CreateFactory(),
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

            var searches = buttons.Where(button =>
                AutomationProperties.GetName(button) == TestUiStrings.English.TextSearch).ToArray();
            Assert.Equal(2, searches.Length);
            Assert.All(searches, button => Assert.IsType<Image>(
                Assert.IsType<ContentControl>(Assert.IsType<StackPanel>(button.Content).Children[0]).Content));

            var provider = Assert.Single(buttons, button => ReferenceEquals(button.Tag, null) &&
                AutomationProperties.GetName(button).Contains("choose provider", StringComparison.Ordinal));
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var yandex = Descendants((DependencyObject)overlay.Content).OfType<Button>()
                .Single(button => Equals(button.Tag, SearchProviderIds.YandexImages));
            yandex.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(SearchProviderIds.YandexImages,
                Assert.IsType<ProviderSelected>(commands[1]).ProviderId);
            Assert.All(searches, button => Assert.IsType<TextBlock>(
                Assert.IsType<ContentControl>(Assert.IsType<StackPanel>(button.Content).Children[0]).Content));
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var google = Descendants((DependencyObject)overlay.Content).OfType<Button>()
                .Single(button => Equals(button.Tag, SearchProviderIds.GoogleLens));
            google.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(3, commands.Count);
            Assert.Equal(SearchProviderIds.GoogleLens,
                Assert.IsType<ProviderSelected>(commands[2]).ProviderId);
            Assert.All(searches, button => Assert.IsType<Image>(
                Assert.IsType<ContentControl>(Assert.IsType<StackPanel>(button.Content).Children[0]).Content));
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
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            overlay.Activate();
            Assert.True(NativeMethods.GetCursorPos(out var originalPointer));
            try
            {
                Assert.Equal(Visibility.Collapsed, overlay.VisualState.Debug.Panel.Visibility);
                overlay.SetDebugPanelOpen(true);
                overlay.UpdateLayout();
                Assert.Equal(Visibility.Visible, overlay.VisualState.Debug.Panel.Visibility);

                var noAudio = overlay.VisualState.Debug.MusicScenarioButtons.Children
                    .OfType<Button>()
                    .Single(button => Equals(button.Tag, MusicDebugScenario.NoAudio));
                ClickWithRealPointer(noAudio);
                PumpUntil(() => commands.Count >= 1);
                ClickWithRealPointer(overlay.VisualState.Music.Button);
                PumpUntil(() => commands.Count >= 2);

                Assert.Equal(
                    MusicDebugScenario.NoAudio,
                    Assert.IsType<MusicDebugScenarioSelected>(commands[0]).Scenario);
                Assert.IsType<StartMusicRecognition>(commands[1]);
                Assert.Equal(Visibility.Collapsed, overlay.VisualState.Debug.Panel.Visibility);
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
    public void Real_pointer_selection_transfers_frame_once_and_repeated_cleanup_preserves_it()
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
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            overlay.Activate();
            Assert.True(NativeMethods.GetCursorPos(out var originalPointer));
            try
            {
                var input = overlay.VisualState.Selection.InputSurface;
                var start = input.PointToScreen(new Point(80, 80));
                var finish = input.PointToScreen(new Point(300, 220));
                Assert.True(SetCursorPos((int)Math.Round(start.X), (int)Math.Round(start.Y)));
                mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                PumpUntil(() => ReferenceEquals(Mouse.Captured, input));
                Assert.True(SetCursorPos((int)Math.Round(finish.X), (int)Math.Round(finish.Y)));
                mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                PumpUntil(() => commands.Count == 1);

                var selected = Assert.IsType<VisualSelection>(Assert.Single(commands));
                Assert.True(overlay.FrameTransferred);
                Assert.Same(frame, selected.Selection.FrozenFrame);
                Assert.True(selected.Selection.Bounds.Width > 0);
                Assert.True(selected.Selection.Bounds.Height > 0);

                overlay.CloseFromSession();
                overlay.CloseFromSession();
                Dispatcher.Run();

                Assert.Equal(640, selected.Selection.FrozenFrame.Width);
                Assert.Single(commands);
            }
            finally
            {
                SetCursorPos(originalPointer.X, originalPointer.Y);
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
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            overlay.ShowListening();
            Assert.Equal(Visibility.Visible, overlay.VisualState.ActivityHost.Visibility);
            Assert.Equal(1, overlay.VisualState.Selection.Dim.Opacity);
            if (OverlayVisualResources.AnimationsEnabled())
                Assert.True(overlay.VisualState.Selection.Screenshot.HasAnimatedProperties);

            overlay.ShowMusicResult(MusicRecognitionOutcome.Matched(new ShazamRecognition(
                "Track", "Artist", null, null, null, null, "https://www.shazam.com/track/1")));
            overlay.UpdateLayout();

            Assert.Equal(OverlayInteractionMode.MusicResult, overlay.Mode);
            Assert.False(overlay.VisualState.Music.Waveform.IsRendering);
            PumpFor(TimeSpan.FromMilliseconds(220));
            Assert.Equal(Visibility.Collapsed, overlay.VisualState.ActivityHost.Visibility);
            Assert.Equal(Visibility.Visible, overlay.VisualState.Music.ResultHost.Visibility);
            Assert.Same(overlay.VisualState.Bottom.ResultSlot, overlay.VisualState.Music.ResultHost.Parent);
            var card = Assert.Single(overlay.VisualState.Music.ResultHost.Children.OfType<Border>());
            Assert.Equal(120, card.ActualHeight);
            Assert.Equal(new CornerRadius(24), card.CornerRadius);
            var actionNames = Descendants(overlay.VisualState.Music.ResultHost).OfType<Button>()
                .Select(AutomationProperties.GetName)
                .ToArray();
            Assert.Contains(TestUiStrings.English.CopyTrackInfo, actionNames);
            Assert.Contains(TestUiStrings.English.OpenInShazam, actionNames);
            Assert.Contains(TestUiStrings.English.Close, actionNames);
            Assert.All(
                Descendants(overlay.VisualState.Music.ResultHost).OfType<Button>()
                    .Where(button => AutomationProperties.GetName(button) != TestUiStrings.English.OpenInShazam),
                button => Assert.Equal(30, button.Width));
            var copy = Descendants(overlay.VisualState.Music.ResultHost).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.CopyTrackInfo);
            Assert.True(copy.Focus());
            MusicOverlayVisualPresenter.SetCopyConfirmed(copy, confirmed: true, TestUiStrings.English, lightTheme: false);
            MusicOverlayVisualPresenter.SetCopyConfirmed(copy, confirmed: false, TestUiStrings.English, lightTheme: false);
            overlay.UpdateLayout();
            var copyChrome = Assert.IsType<Border>(copy.Template.FindName("Chrome", copy));
            var restoredCopyIcon = Assert.IsType<System.Windows.Shapes.Path>(copy.Content);

            Assert.Equal(new Thickness(0), copyChrome.BorderThickness);
            Assert.Equal(12, restoredCopyIcon.Width);
            Assert.Equal(12, restoredCopyIcon.Height);
            var copyIconOrigin = restoredCopyIcon.TranslatePoint(new Point(), copy);
            Assert.InRange(copyIconOrigin.X, 0, copy.ActualWidth - restoredCopyIcon.ActualWidth);
            Assert.InRange(copyIconOrigin.Y, 0, copy.ActualHeight - restoredCopyIcon.ActualHeight);
            var close = Descendants(overlay.VisualState.Music.ResultHost).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close);
            Assert.Equal(12, Assert.IsType<System.Windows.Shapes.Path>(close.Content).Width);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.IsType<DismissMusicResult>(Assert.Single(commands));
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            if (OverlayVisualResources.AnimationsEnabled())
            {
                Assert.Equal(Visibility.Visible, overlay.VisualState.Music.ResultHost.Visibility);
                Assert.False(overlay.VisualState.Music.ResultHost.IsHitTestVisible);
                Assert.Single(overlay.VisualState.Music.ResultHost.Children);
                PumpFor(TimeSpan.FromMilliseconds(220));
            }
            Assert.Equal(Visibility.Collapsed, overlay.VisualState.Music.ResultHost.Visibility);
            Assert.Empty(overlay.VisualState.Music.ResultHost.Children);
            Assert.False(overlay.Dispatcher.HasShutdownStarted);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Retry_result_returns_to_listening_and_publishes_once()
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
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.ShowListening();
            overlay.ShowMusicResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch));
            overlay.UpdateLayout();
            var retry = Descendants(overlay.VisualState.Music.ResultHost).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.TryAgain);

            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
            Assert.Equal(Visibility.Visible, overlay.VisualState.ActivityHost.Visibility);
            Assert.IsType<RetryMusicRecognition>(Assert.Single(commands));
            if (OverlayVisualResources.AnimationsEnabled())
            {
                Assert.Equal(Visibility.Visible, overlay.VisualState.Music.ResultHost.Visibility);
                Assert.False(overlay.VisualState.Music.ResultHost.IsHitTestVisible);
                Assert.Single(overlay.VisualState.Music.ResultHost.Children);
                PumpFor(TimeSpan.FromMilliseconds(220));
            }
            Assert.Equal(Visibility.Collapsed, overlay.VisualState.Music.ResultHost.Visibility);
            Assert.Empty(overlay.VisualState.Music.ResultHost.Children);
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
    [InlineData(MusicRecognitionStatus.Matched)]
    public void State_card_statuses_show_expected_content_and_close(
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
            MusicOverlayVisualPresenter.PresentResult(
                visual.Music,
                MusicRecognitionOutcome.From(status),
                TestUiStrings.English,
                lightTheme: false,
                commands.Add,
                (_, _) => { });
            var expectedMessage = status switch
            {
                MusicRecognitionStatus.NoMatch => TestUiStrings.English.MusicNoMatch,
                MusicRecognitionStatus.NoAudio => TestUiStrings.English.MusicNoAudio,
                MusicRecognitionStatus.RateLimited => TestUiStrings.English.MusicRateLimited,
                MusicRecognitionStatus.DeviceError => TestUiStrings.English.MusicDeviceError,
                _ => TestUiStrings.English.MusicNetworkError,
            };
            var expectedAction = status == MusicRecognitionStatus.RateLimited
                ? null
                : status == MusicRecognitionStatus.NoMatch
                    ? TestUiStrings.English.TryAgain
                    : TestUiStrings.English.Retry;
            Assert.Contains(
                Descendants(visual.Music.ResultHost).OfType<TextBlock>(),
                text => text.Text == expectedMessage);
            var actionNames = Descendants(visual.Music.ResultHost).OfType<Button>()
                .Select(AutomationProperties.GetName)
                .Where(name => name != TestUiStrings.English.Close)
                .ToArray();
            if (expectedAction is null)
                Assert.Empty(actionNames);
            else
                Assert.Equal([expectedAction], actionNames);
            var close = Descendants(visual.Music.ResultHost).OfType<Button>()
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

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
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
