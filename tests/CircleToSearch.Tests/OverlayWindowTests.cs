using System.Windows.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class OverlayWindowTests
{
    private static readonly SearchProviderDescriptor[] Providers =
    [
        new(SearchProviderIds.GoogleLens, "Google Lens"),
        new(SearchProviderIds.YandexImages, "Yandex Images"),
    ];

    [Fact]
    public void Visual_factory_places_music_button_beside_not_inside_chip()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);

            Assert.Same(visual.Actions.Chip, visual.Actions.Tray.Children[0]);
            Assert.Same(visual.Music.Button, visual.Actions.Tray.Children[1]);
            Assert.DoesNotContain(visual.Music.Button, Descendants(visual.Actions.Chip));
            Assert.Equal(TestUiStrings.English.MusicRecognitionAction, visual.Music.Button.ToolTip);
            Assert.Equal(
                TestUiStrings.English.MusicRecognitionAction,
                AutomationProperties.GetName(visual.Music.Button));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Music_button_finishes_with_music_action_without_selection()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                new OverlayControllerFactory());
            overlay.Show();
            var musicButton = Assert.Single(
                Descendants((DependencyObject)overlay.Content).OfType<Button>(),
                button => AutomationProperties.GetName(button) == TestUiStrings.English.MusicRecognitionAction);
            musicButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.Run();

            Assert.Equal(OverlayAction.MusicRecognition, overlay.Outcome?.Action);
            Assert.Null(overlay.Outcome?.Selection);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Action_tray_hit_test_blocks_lasso_even_when_original_source_is_the_window()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                new OverlayControllerFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            var musicButton = Assert.Single(
                Descendants((DependencyObject)overlay.Content).OfType<Button>(),
                button => AutomationProperties.GetName(button) == TestUiStrings.English.MusicRecognitionAction);
            var center = musicButton.TransformToAncestor(overlay).Transform(
                new Point(musicButton.ActualWidth / 2, musicButton.ActualHeight / 2));

            Assert.True(overlay.IsActionTrayInteraction(overlay, center));
            Assert.False(overlay.IsActionTrayInteraction(overlay, new Point(10, 10)));

            overlay.CancelFromCoordinator();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Entrance_ripple_starts_on_a_rendered_scene_layer()
    {
        var failure = RunOnSta(() =>
        {
            if (!OverlayVisualResources.AnimationsEnabled()) return;
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                new OverlayControllerFactory(),
                overscan: false,
                entranceOrigin: new GdiPoint(320, 200));
            overlay.Show();

            var renderFrame = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                renderFrame.Continue = false;
            };
            timeout.Start();
            Dispatcher.PushFrame(renderFrame);

            Assert.True(overlay.VisualState.Effects.SceneRippleLayer.ActualWidth > 0);
            Assert.True(overlay.VisualState.Effects.SceneRippleLayer.ActualHeight > 0);
            Assert.Equal(1, overlay.VisualState.Effects.SceneRipples.ActiveCount);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Overlay_opens_and_shuts_down_without_dispatcher_exception()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                new OverlayControllerFactory());
            overlay.Show();
            PumpUntilShutdown(overlay);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Overlay_on_negative_coordinates_monitor_opens_without_dispatcher_exception()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(100, 80);
            var monitor = new GdiRectangle(-1920, -80, 100, 80);
            var workArea = new GdiRectangle(-1920, -80, 100, 50);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                workArea,
                1.25,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                new OverlayControllerFactory());
            overlay.Show();
            PumpUntilShutdown(overlay);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Cancel_from_coordinator_shuts_the_dispatcher_down()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                new OverlayControllerFactory());
            overlay.Show();
            overlay.CancelFromCoordinator();
            Dispatcher.Run();
            Assert.Null(overlay.Outcome);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Session_close_forces_shutdown_after_cancel_has_already_started()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                new OverlayControllerFactory());
            overlay.Show();
            overlay.CancelFromCoordinator();
            overlay.Dispatcher.BeginInvoke(overlay.CloseFromSession, DispatcherPriority.Background);
            Dispatcher.Run();

            Assert.True(overlay.Dispatcher.HasShutdownFinished);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Escape_closes_debug_then_provider_before_publishing_cancel_once()
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
                new OverlayControllerFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            overlay.SetDebugPanelOpen(true);
            overlay.VisualState.Provider!.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            RaiseEscape(overlay);
            Assert.Equal(Visibility.Collapsed, overlay.VisualState.Debug.Panel.Visibility);
            Assert.Empty(commands);

            RaiseEscape(overlay);
            Assert.Empty(commands);

            RaiseEscape(overlay);
            overlay.CancelFromCoordinator();
            Assert.IsType<CancelSession>(Assert.Single(commands));
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Starting_music_recognition_closes_the_debug_panel()
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
                new OverlayControllerFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            overlay.SetDebugPanelOpen(true);

            overlay.VisualState.Music.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Visibility.Collapsed, overlay.VisualState.Debug.Panel.Visibility);
            Assert.IsType<StartMusicRecognition>(Assert.Single(commands));
            Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Small_selection_resets_and_shows_toast_without_publishing_a_command()
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
                    new OverlayOptions(8, 10_000),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                new OverlayControllerFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            overlay.Activate();
            var input = overlay.VisualState.Selection.InputSurface;
            Assert.True(NativeMethods.GetCursorPos(out var originalPointer));
            var start = input.PointToScreen(new Point(100, 100));
            Assert.True(SetCursorPos((int)Math.Round(start.X), (int)Math.Round(start.Y)));
            PumpFor(TimeSpan.FromMilliseconds(40));
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            PumpUntil(() => ReferenceEquals(System.Windows.Input.Mouse.Captured, input));
            Assert.True(SetCursorPos((int)Math.Round(start.X + 10), (int)Math.Round(start.Y)));
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
            PumpUntil(() => overlay.VisualState.Bottom.Stack.Children.Count == 3);

            Assert.False(overlay.FrameTransferred);
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            Assert.Empty(commands);
            Assert.Empty(overlay.VisualState.Selection.Halo.Points);
            Assert.Empty(overlay.VisualState.Selection.Accent.Points);
            Assert.Same(System.Windows.Media.Geometry.Empty, overlay.VisualState.Selection.Sheen.Data);
            Assert.Same(System.Windows.Media.Geometry.Empty, overlay.VisualState.Selection.DimRect.Data);
            Assert.Same(System.Windows.Media.Geometry.Empty, overlay.VisualState.Selection.SelectionFrame.Data);
            Assert.Equal(1, overlay.VisualState.Selection.Dim.Opacity);
            Assert.Equal(0, overlay.VisualState.Selection.DimRect.Opacity);
            Assert.True(overlay.VisualState.Actions.Tray.IsHitTestVisible);

            var stack = overlay.VisualState.Bottom.Stack.Children.Cast<UIElement>().ToArray();
            Assert.Equal(3, stack.Length);
            var toastSlot = Assert.IsType<Grid>(stack[0]);
            Assert.Same(overlay.VisualState.Bottom.ResultSlot, stack[1]);
            Assert.Same(overlay.VisualState.Bottom.ActionSlot, stack[2]);
            var toastCard = Assert.IsType<Border>(Assert.Single(toastSlot.Children));
            var toastMessage = Assert.IsType<TextBlock>(toastCard.Child);
            Assert.Equal(TestUiStrings.English.SelectionTooSmall, toastMessage.Text);

            RaiseEscape(overlay);
            Assert.IsType<CancelSession>(Assert.Single(commands));
            Dispatcher.Run();
            SetCursorPos(originalPointer.X, originalPointer.Y);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Exact_click_copies_frozen_pixel_keeps_toast_for_confirmation_then_publishes_terminal_command()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var clipboard = new List<string>();
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
                new OverlayControllerFactory(clipboard.Add, () => false),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            var local = new Point(120, 80);
            frame.SetPixel((int)local.X, (int)local.Y, System.Drawing.Color.FromArgb(0x3A, 0x7B, 0xD5));
            Assert.Equal(
                SelectionGestureKind.PixelPick,
                SelectionGestureClassifier.Classify([new GdiPoint((int)local.X, (int)local.Y)], 12, 3));
            GetColorPicker(overlay).Pick(new GdiPoint((int)local.X, (int)local.Y));

            Assert.Equal(["#3A7BD5"], clipboard);
            Assert.Empty(commands);
            Assert.Equal(OverlayInteractionMode.ColorConfirmation, overlay.Mode);
            Assert.False(overlay.FrameTransferred);
            Assert.False(overlay.VisualState.Selection.InputSurface.IsMouseCaptured);
            Assert.False(overlay.VisualState.Actions.Tray.IsHitTestVisible);
            var toastSlot = Assert.IsType<Grid>(overlay.VisualState.Bottom.Stack.Children[0]);
            var toastMessage = Assert.IsType<TextBlock>(Assert.IsType<Border>(Assert.Single(toastSlot.Children)).Child);
            Assert.Equal("Copied: #3A7BD5", AutomationProperties.GetName(toastMessage));

            Dispatcher.Run();

            Assert.IsType<ColorCopied>(Assert.Single(commands));
            Assert.Equal(OverlayInteractionMode.Closing, overlay.Mode);
            Assert.False(overlay.FrameTransferred);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Clipboard_failure_returns_to_selecting_with_tray_and_localized_error()
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
                new OverlayControllerFactory(
                    _ => throw new InvalidOperationException("clipboard busy"),
                    () => false),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            GetColorPicker(overlay).Pick(new GdiPoint(120, 80));

            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            Assert.False(overlay.FrameTransferred);
            Assert.Empty(commands);
            Assert.True(overlay.VisualState.Actions.Tray.IsHitTestVisible);
            var toastSlot = Assert.IsType<Grid>(overlay.VisualState.Bottom.Stack.Children[0]);
            var toastMessage = Assert.IsType<TextBlock>(Assert.IsType<Border>(Assert.Single(toastSlot.Children)).Child);
            Assert.Equal(TestUiStrings.English.ColorCopyFailed, toastMessage.Text);

            RaiseEscape(overlay);
            Dispatcher.Run();
            Assert.IsType<CancelSession>(Assert.Single(commands));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Debug_toast_buttons_spawn_all_tones_in_the_overlay_stack()
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
                new OverlayControllerFactory(_ => { }, () => false),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            foreach (var button in overlay.VisualState.Debug.ToastButtons.Children.OfType<Button>())
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var stack = overlay.VisualState.Bottom.Stack.Children.Cast<UIElement>().ToArray();
            Assert.Equal(5, stack.Length);
            Assert.Equal(
                [
                    TestUiStrings.English.DebugToastNeutral,
                    TestUiStrings.English.DebugToastError,
                    TestUiStrings.English.DebugToastSuccess,
                ],
                stack.Take(3)
                    .Select(slot => Assert.IsType<TextBlock>(
                        Assert.IsType<Border>(
                            Assert.Single(Assert.IsType<Grid>(slot).Children)).Child).Text)
                    .ToArray());
            Assert.Same(overlay.VisualState.Bottom.ResultSlot, stack[3]);
            Assert.Same(overlay.VisualState.Bottom.ActionSlot, stack[4]);

            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    private static void PumpUntilShutdown(OverlayWindow overlay)
    {
        overlay.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        Dispatcher.Run();
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
        Assert.True(condition(), "The pointer gesture was not delivered to the overlay.");
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

    private static ColorPickController GetColorPicker(OverlayWindow overlay)
    {
        var field = typeof(OverlayWindow).GetField(
            "_controllers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return Assert.IsType<OverlayControllers>(field?.GetValue(overlay)).ColorPick;
    }

    private static void RaiseEscape(OverlayWindow overlay)
    {
        overlay.RaiseEvent(new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(overlay),
            0,
            System.Windows.Input.Key.Escape)
        {
            RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
        });
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.True(!thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
