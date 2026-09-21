namespace CircleToSearch.Capture;

using System.Globalization;
using System.Text.RegularExpressions;
using CircleToSearch.Ui;
using System.Windows.Media.Imaging;

internal sealed class ClipboardCopyService
{
    private const int PreviewTextElementLimit = 67;
    private readonly Action<string> _setClipboard;
    private readonly Action<ToastNotification> _showToast;
    private readonly UiStrings _strings;
    private readonly Action<BitmapSource>? _setImage;

    internal ClipboardCopyService(
        Action<string> setClipboard,
        Action<ToastNotification> showToast,
        UiStrings strings,
        Action<BitmapSource>? setImage = null)
    {
        _setClipboard = setClipboard ?? throw new ArgumentNullException(nameof(setClipboard));
        _showToast = showToast ?? throw new ArgumentNullException(nameof(showToast));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _setImage = setImage;
    }

    internal bool TryCopyImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        try
        {
            if (_setImage is null) throw new InvalidOperationException("Image clipboard is unavailable.");
            _setImage(image);
        }
        catch
        {
            _showToast(new ToastNotification(_strings.CopyFailed, ToastTone.Error));
            return false;
        }
        _showToast(new ToastNotification(_strings.ImageCopied, ToastTone.Success));
        return true;
    }

    internal bool TryCopy(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            _setClipboard(text);
        }
        catch
        {
            _showToast(new ToastNotification(_strings.CopyFailed, ToastTone.Error));
            return false;
        }

        _showToast(new ToastNotification(_strings.CopiedText(BuildPreview(text)), ToastTone.Success));
        return true;
    }

    private static string BuildPreview(string text)
    {
        var normalized = Regex.Replace(text.Trim(), @"\s+", " ");
        var elementIndexes = StringInfo.ParseCombiningCharacters(normalized);
        if (elementIndexes.Length <= PreviewTextElementLimit) return normalized;
        return normalized[..elementIndexes[PreviewTextElementLimit]].TrimEnd() + "...";
    }
}
