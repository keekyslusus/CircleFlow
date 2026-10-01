using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;
using Xunit;

namespace CircleToSearch.Tests;

// Renders the lasso mid-draw, the finished selection rectangle, and two entrance-effect
// frames to PNGs without running a real selection:
// CTS_SELECTION_PREVIEW=1 dotnet test --filter SelectionPreviewTests.
// Output: tests/temp/selection-preview-{lasso,frame,entrance-1,entrance-2}.png, 150% DPI.
public sealed class SelectionPreviewTests
{
    [SkippableFact]
    public void Renders_lasso_and_selection_frame_previews()
    {
        TestSwitches.Require("CTS_SELECTION_PREVIEW");

        var directory = TestOutputPaths.TempDirectory;
        Directory.CreateDirectory(directory);
        Assert.Null(RunOnSta(() => Render(
            Path.Combine(directory, "selection-preview-lasso.png"),
            Path.Combine(directory, "selection-preview-frame.png"),
            Path.Combine(directory, "selection-preview-entrance-1.png"),
            Path.Combine(directory, "selection-preview-entrance-2.png"))));
    }

    private static void Render(string lassoPath, string framePath, string entrancePath1, string entrancePath2)
    {
        const int width = 960;
        const int height = 600;
        var size = new Size(width, height);
        var visual = OverlayVisualFactory.CreateRoot(null, size, 32, lightTheme: false, TestUiStrings.English);
        // The screenshot layer is empty in the preview, so give the root a desktop-like gradient.
        visual.Root.Background = new LinearGradientBrush(
            Color.FromRgb(0xEA, 0xEE, 0xF3),
            Color.FromRgb(0x22, 0x26, 0x32),
            35);
        var window = new Window
        {
            Content = visual.Root,
            Width = width,
            Height = height,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();

        // Draw state, captured after the first render pass.
        var drawn = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        drawn.Tick += (_, _) =>
        {
            drawn.Stop();
            var stroke = EllipseStroke(new Point(width / 2.0, height / 2.0), 210, 130, 48);
            visual.Selection.Dim.Data = SelectionOverlayTransitions.BuildRevealGeometry(size, stroke);
            visual.Selection.Sheen.Data = SelectionOverlayTransitions.BuildPolygonGeometry(stroke);
            visual.Selection.Halo.Points = new PointCollection(stroke);
            visual.Selection.Accent.Points = new PointCollection(stroke);
            Capture(visual.Root, lassoPath);

            // Finished state: same snap the window performs on mouse-up.
            var snapped = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            snapped.Tick += (_, _) =>
            {
                snapped.Stop();
                var rect = BoundsOf(stroke);
                rect.Inflate(8, 8);
                Point[] corners =
                [
                    new(rect.Left, rect.Top),
                    new(rect.Right, rect.Top),
                    new(rect.Right, rect.Bottom),
                    new(rect.Left, rect.Bottom),
                ];
                SelectionOverlayTransitions.BeginSelectionReveal(
                    visual.Selection,
                    SelectionOverlayTransitions.BuildRevealGeometry(size, corners),
                    SelectionOverlayTransitions.BuildSelectionFrameGeometry(rect));

                // Entrance effect frames, captured mid-wave.
                var framed = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                framed.Tick += (_, _) =>
                {
                    framed.Stop();
                    Capture(visual.Root, framePath);
                    visual.Effects.SceneRipples.Emit(new SceneRippleRequest(
                        new Point(width * 0.42, height * 0.45),
                        SceneRipplePreset.Entrance,
                        1));
                    var wave1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                    wave1.Tick += (_, _) =>
                    {
                        wave1.Stop();
                        Capture(visual.Root, entrancePath1);
                        var wave2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(340) };
                        wave2.Tick += (_, _) =>
                        {
                            wave2.Stop();
                            Capture(visual.Root, entrancePath2);
                            window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                        };
                        wave2.Start();
                    };
                    wave1.Start();
                };
                framed.Start();
            };
            snapped.Start();
        };
        drawn.Start();
        Dispatcher.Run();
    }

    private static List<Point> EllipseStroke(Point center, double radiusX, double radiusY, int count)
    {
        var points = new List<Point>(count + 1);
        for (var i = 0; i <= count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            var wobble = 1 + 0.08 * Math.Sin(3 * angle + 1);
            points.Add(new Point(
                center.X + Math.Cos(angle) * radiusX * wobble,
                center.Y + Math.Sin(angle) * radiusY * wobble));
        }
        return points;
    }

    private static Rect BoundsOf(IReadOnlyList<Point> points)
    {
        var rect = Rect.Empty;
        foreach (var point in points) rect.Union(point);
        return rect;
    }

    private static void Capture(UIElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1440, 900, 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
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
}
