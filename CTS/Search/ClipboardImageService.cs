using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CircleToSearch.Interop;

namespace CircleToSearch.Search;

// WPF clipboard access requires an STA thread; the caller hands in a dispatcher instead of
// owning one. PNG is placed both as raw "PNG"/"image/png" registered formats (preferred by
// Chromium) and as a DIB via SetImage (universal fallback).
public static class ClipboardImageService
{
    public static bool TryCopy(StaDispatcher dispatcher, byte[] png)
    {
        try
        {
            return dispatcher.InvokeAsync<bool>(() => Copy(png), CancellationToken.None)
                .GetAwaiter()
                .GetResult() == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool Copy(byte[] png)
    {
        var data = new DataObject();
        var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        data.SetImage(frame);
        data.SetData("PNG", new MemoryStream(png));
        data.SetData("image/png", new MemoryStream(png));
        Clipboard.SetDataObject(data, true);
        return true;
    }
}
