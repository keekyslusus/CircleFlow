using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Shell.Notifications;
using Xunit;
using static CircleToSearch.Tests.WpfUi;

namespace CircleToSearch.Tests;

// CTS_NOTIFICATION_PREVIEW=1 dotnet test --filter NotificationPreviewTests
// Output: tests/temp/notification-preview-{dark,light}.png at 150% DPI.
public sealed class NotificationPreviewTests
{
    [Fact]
    public void Renders_a_plain_message_an_error_and_an_update_offer_in_both_themes() => OnSta(time =>
    {
        if (Environment.GetEnvironmentVariable("CTS_NOTIFICATION_PREVIEW") != "1") return;
        Directory.CreateDirectory(TestOutputPaths.TempDirectory);
        Render(time, lightTheme: false, "dark", Color.FromRgb(0x1E, 0x21, 0x27));
        Render(time, lightTheme: true, "light", Color.FromRgb(0xDD, 0xE3, 0xEA));
    });

    private static void Render(ManualAnimationClock time, bool lightTheme, string name, Color backdrop)
    {
        var strings = TestUiStrings.English;
        using var presenter = new NotificationPresenter(Dispatcher.CurrentDispatcher, strings,
            new AppPaths(AppContext.BaseDirectory).TrayIconPath, () => lightTheme,
            new PluginLog(Path.Combine(TestOutputPaths.TempDirectory, "notification-preview-log")), TimeSpan.FromMinutes(5));
        presenter.ShowMessage(strings.PluginTitle, strings.MusicNoMatch);
        presenter.ShowError(strings.PluginTitle, strings.HotkeyConflict("Ctrl+Shift+S"));
        presenter.ShowMessageWithButton(strings.UpdateAvailableTitle, strings.UpdateAvailable("0.5.2"), strings.UpdateInstall, () => { });
        Pump();
        time.Advance(400);
        var window = presenter.Window!;
        window.Background = new SolidColorBrush(backdrop);
        Pump();
        var stack = (FrameworkElement)window.FindName("Cards");
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * scale), (int)(window.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var height = (int)((stack.ActualHeight + 40) * scale);
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(0, bitmap.PixelHeight - height, bitmap.PixelWidth, height));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var file = File.Create(Path.Combine(TestOutputPaths.TempDirectory, $"notification-preview-{name}.png"));
        encoder.Save(file);
    }
}
