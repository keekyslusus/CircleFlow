using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace CircleToSearch.Tests;

// Shared by the window tests: tree walks, a dispatcher thread with a manual animation clock, and PNG previews.
internal static class WpfUi
{
    public static IEnumerable<DependencyObject> LogicalChildren(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in LogicalChildren(child)) yield return nested;
        }
    }

    public static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in VisualChildren(child)) yield return nested;
        }
    }

    // UseLayoutRounding snaps every edge to a whole device pixel, so at 125% scale an 18 DIP element lays out
    // as 17.6 or 18.4. One snapped value may drift by half a pixel, a distance between two snapped edges by a whole one.
    public static double LayoutRoundingTolerance(Visual visual) => 0.5 / VisualTreeHelper.GetDpi(visual).DpiScaleX + 1e-6;

    public static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    public static void PumpUntil(Func<bool> condition, string timeoutMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline) Pump();
        Assert.True(condition(), timeoutMessage);
    }

    public static RenderTargetBitmap Render(FrameworkElement root)
    {
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    public static void SavePng(FrameworkElement root, string filename)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(root)));
        Directory.CreateDirectory(TestOutputPaths.TempDirectory);
        using var stream = File.Create(Path.Combine(TestOutputPaths.TempDirectory, filename));
        encoder.Save(stream);
    }

    public static void OnSta(Action<ManualAnimationClock> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var time = ManualAnimationClock.Install();
                action(time);
            }
            catch (Exception exception) { error = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(40)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
