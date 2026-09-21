using System.IO;
using System.Globalization;
using System.Windows.Media.Imaging;
using CircleToSearch.Ui;
using Microsoft.Win32;

namespace CircleToSearch.Capture;

internal sealed class ImageSaveService(Func<Action, Task> dispatch, UiStrings strings, IPluginNotifier notifier)
{
    internal Task SaveAsync(BitmapSource image) => dispatch(() =>
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = strings.ImageSaveTitle,
                Filter = strings.ImageSaveFilter,
                DefaultExt = ".png",
                FileName = string.Format(CultureInfo.InvariantCulture, strings.ImageFileName, DateTime.Now),
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog() != true) return;
            using var stream = dialog.OpenFile();
            Encode(image, stream, string.Equals(Path.GetExtension(dialog.FileName), ".png", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            notifier.ShowError(strings.PluginTitle, strings.SavingFailed(exception.Message));
        }
    });

    internal static void Encode(BitmapSource image, Stream stream, bool png)
    {
        BitmapEncoder encoder = png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }
}
