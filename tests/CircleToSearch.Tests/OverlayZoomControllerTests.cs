using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Search;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class OverlayZoomControllerTests
{
    private const double Width = 640;
    private const double Height = 400;

    [Fact]
    public void Keyboard_zoom_magnifies_the_screen_layers_together_and_keeps_the_lasso_thin()
    {
        RunOnSta(time =>
        {
            var (window, root, selection, highlights) = Scene();
            var zoomedChanges = new List<bool>();
            var viewChanges = 0;
            using var zoom = new OverlayZoomController(root, selection, highlights, root, () => true);
            zoom.ZoomedChanged += zoomedChanges.Add;
            zoom.ViewChanged += () => viewChanges++;
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.True(zoom.TryHandleShortcut(Key.OemPlus, ModifierKeys.Control));
                time.Advance(1000);

                Assert.Equal(1.25, zoom.Scale, 4);
                Assert.Equal([true], zoomedChanges);
                Assert.True(viewChanges > 1);
                var scenePoint = new Point(100, 80);
                var expected = zoom.ToViewport(scenePoint);
                foreach (UIElement layer in new UIElement[]
                         {
                             selection.Dim, selection.Halo, selection.Accent, selection.SelectionFrame, highlights,
                             selection.InputSurface,
                         })
                    AssertNear(expected, layer.TranslatePoint(scenePoint, root));
                // The screenshot sits one dip in for overscan, so the same screen point is one dip less in its space.
                AssertNear(expected, selection.Screenshot.TranslatePoint(scenePoint - new Vector(1, 1), root));
                Assert.Equal(12 / 1.25, selection.Halo.StrokeThickness, 4);
                Assert.Equal(2.5 / 1.25, selection.Accent.StrokeThickness, 4);
                // Blurs are left out when the GPU cannot render them.
                if (selection.Halo.Effect is BlurEffect blur) Assert.Equal(8 / 1.25, blur.Radius, 4);
                Assert.Equal(18 / 1.25, ((DropShadowEffect)selection.SelectionFrame.Effect).BlurRadius, 4);

                Assert.True(zoom.TryHandleShortcut(Key.D0, ModifierKeys.Control));
                time.Advance(1000);

                Assert.Equal(1, zoom.Scale);
                Assert.Equal([true, false], zoomedChanges);
                Assert.True(selection.Screenshot.RenderTransform.Value.IsIdentity);
                Assert.Equal(12, selection.Halo.StrokeThickness, 6);
                Assert.False(zoom.TryHandleShortcut(Key.D0, ModifierKeys.Control));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Transparent_overlay_stays_a_layered_window()
    {
        RunOnSta(_ =>
        {
            using var frame = new GdiBitmap(640, 400);
            var commands = new List<IOverlayCommand>();
            var window = ZoomOverlay(frame, commands);
            try
            {
                Assert.True(window.AllowsTransparency);
            }
            finally
            {
                window.CloseFromSession();
                foreach (var command in commands) OverlayCommandOwnership.DisposePayload(command);
            }
        });
    }

    [Fact]
    public void Zoom_reports_its_travel_and_a_push_past_the_maximum()
    {
        RunOnSta(time =>
        {
            var (window, root, selection, highlights) = Scene();
            var travel = 0.0;
            var limits = 0;
            using var zoom = new OverlayZoomController(root, selection, highlights, root, () => true,
                moved => travel += moved, () => limits++);
            try
            {
                window.Show();
                window.UpdateLayout();
                for (var notch = 0; notch < 10; notch++) zoom.TryHandleShortcut(Key.OemPlus, ModifierKeys.Control);
                time.Advance(1500);
                Assert.Equal(Math.Log(OverlayZoomCamera.MaxScale), travel, 4);
                Assert.Equal(0, limits);

                zoom.TryHandleShortcut(Key.OemPlus, ModifierKeys.Control);
                Assert.Equal(1, limits);
                time.Advance(50);
                Assert.True(zoom.Scale > OverlayZoomCamera.MaxScale);
                time.Advance(1500);
                Assert.Equal(OverlayZoomCamera.MaxScale, zoom.Scale, 6);
                Assert.Equal(Math.Log(OverlayZoomCamera.MaxScale), travel, 4);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Backing_away_below_the_screen_shows_black_around_it_only_while_it_lasts()
    {
        RunOnSta(time =>
        {
            var (window, root, selection, highlights) = Scene();
            var limits = 0;
            using var zoom = new OverlayZoomController(root, selection, highlights, root, () => true,
                limitReached: () => limits++);
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.True(zoom.TryHandleShortcut(Key.OemMinus, ModifierKeys.Control));
                time.Advance(80);

                Assert.Equal(1, limits);
                Assert.True(zoom.Scale < 0.995);
                Assert.Equal(PluginPaletteBlack(), (root.Background as SolidColorBrush)?.Color);
                time.Advance(1500);
                Assert.Equal(1, zoom.Scale, 6);
                Assert.Null(root.Background);
                Assert.True(selection.Screenshot.RenderTransform.Value.IsIdentity);
            }
            finally { window.Close(); }
        });
    }

    private static Color PluginPaletteBlack() => CircleToSearch.Ui.PluginPalette.OpaqueBlack;

    [Fact]
    public void Zoom_ignores_keys_without_control_and_while_the_overlay_refuses_input()
    {
        RunOnSta(time =>
        {
            var (window, root, selection, highlights) = Scene();
            var canZoom = false;
            using var zoom = new OverlayZoomController(root, selection, highlights, root, () => canZoom);
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.False(zoom.TryHandleShortcut(Key.OemPlus, ModifierKeys.Control));
                canZoom = true;
                Assert.False(zoom.TryHandleShortcut(Key.OemPlus, ModifierKeys.None));
                Assert.False(zoom.TryHandleShortcut(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
                time.Advance(200);

                Assert.Equal(1, zoom.Scale);
                Assert.True(selection.Dim.RenderTransform.Value.IsIdentity);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Escape_or_mouse_back_glides_back_from_zoom_before_closing_the_overlay(bool mouseBack)
    {
        RunOnSta(time =>
        {
            using var frame = new GdiBitmap(640, 400);
            var commands = new List<IOverlayCommand>();
            var window = ZoomOverlay(frame, commands);
            try
            {
                window.Show();
                window.UpdateLayout();
                var screenshot = window.VisualState.Selection.Screenshot;
                Assert.True(window.TryHandleShortcut(Key.OemPlus, ModifierKeys.Control));
                time.Advance(1000);
                Assert.Equal(1.25, screenshot.RenderTransform.Value.M11, 4);
                var hint = window.VisualState.Actions.Hint!;
                Assert.Equal(TestUiStrings.English.SelectionHintPan, hint.Action.Text);

                StepBack(window, mouseBack);
                // The first frame only starts the clock; the second one moves.
                time.Advance(32);

                Assert.InRange(screenshot.RenderTransform.Value.M11, 1.0001, 1.2499);
                time.Advance(1000);
                Assert.True(screenshot.RenderTransform.Value.IsIdentity);
                Assert.NotEqual(TestUiStrings.English.SelectionHintPan, hint.Action.Text);
                Assert.Empty(commands.OfType<CancelSession>());

                if (mouseBack) return;
                StepBack(window, mouseBack: false);
                Assert.Single(commands.OfType<CancelSession>());
            }
            finally
            {
                window.CloseFromSession();
                foreach (var command in commands) OverlayCommandOwnership.DisposePayload(command);
            }
        });
    }

    [Fact]
    public void Escape_in_the_middle_of_a_gesture_still_closes_the_zoomed_overlay()
    {
        RunOnSta(time =>
        {
            using var frame = new GdiBitmap(640, 400);
            var commands = new List<IOverlayCommand>();
            var window = ZoomOverlay(frame, commands);
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.True(window.TryHandleShortcut(Key.OemPlus, ModifierKeys.Control));
                time.Advance(1000);
                var input = window.VisualState.Selection.InputSurface;
                input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                    { RoutedEvent = UIElement.MouseRightButtonDownEvent, Source = input });

                StepBack(window, mouseBack: false);

                Assert.Single(commands.OfType<CancelSession>());
            }
            finally
            {
                window.CloseFromSession();
                foreach (var command in commands) OverlayCommandOwnership.DisposePayload(command);
            }
        });
    }

    private static OverlayWindow ZoomOverlay(GdiBitmap frame, List<IOverlayCommand> commands)
    {
        var monitor = new GdiRectangle(0, 0, frame.Width, frame.Height);
        return new OverlayWindow(frame, monitor, monitor, 1,
            new OverlayLaunchOptions(new OverlayOptions(0, 12), TestUiStrings.English,
                [new(SearchProviderIds.GoogleLens, SearchProviderIds.GoogleLens)], SearchProviderIds.GoogleLens,
                new SearchSessionOptions(Zoom: true)),
            commands.Add,
            TestOverlayControllers.CreateFactory(animationsEnabled: () => false,
                pointerPosition: _ => new Point(300, 100)),
            overscan: false);
    }

    private static void StepBack(OverlayWindow window, bool mouseBack)
    {
        if (mouseBack)
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.XButton1)
                { RoutedEvent = Mouse.PreviewMouseUpEvent });
        else
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0,
                Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static (Window Window, Grid Root, SelectionOverlayVisual Selection, Canvas Highlights) Scene()
    {
        var frame = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 4 * 4);
        var selection = SelectionOverlayVisualFactory.Create(frame, new Size(Width, Height), Colors.CornflowerBlue);
        selection.Screenshot.Margin = new Thickness(1);
        var highlights = new Canvas { IsHitTestVisible = false };
        var root = new Grid();
        foreach (var layer in new UIElement[]
                 {
                     selection.Screenshot, selection.DimLayer, selection.Sheen, selection.Halo,
                     selection.Accent, selection.SelectionFrame, highlights, selection.InputSurface,
                 })
            root.Children.Add(layer);
        // Far off screen, so the real pointer never lands on it and keyboard zoom pivots on the center.
        var window = new Window
        {
            Width = Width,
            Height = Height,
            Left = -20000,
            Top = -20000,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = root,
        };
        return (window, root, selection, highlights);
    }

    private static void AssertNear(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
    }

    private static void RunOnSta(Action<ManualAnimationClock> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var time = ManualAnimationClock.Install();
                action(time);
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
