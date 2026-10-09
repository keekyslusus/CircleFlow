using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CircleToSearch.Interop;

namespace CircleToSearch.Ui;

// Per-pixel transparency composed by DWM. AllowsTransparency makes a window layered, and a layered window is copied
// from the GPU back to memory on every frame; full-screen motion at 4K then runs at half the refresh rate. A
// DWM-composed window keeps rendering straight to the GPU. Unlike a layered one, it takes clicks even with
// WS_EX_TRANSPARENT set.
internal static class DwmTransparentWindow
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDoNotRound = 1;

    // Software rendering hands DWM no alpha, so without a GPU a window stays layered.
    internal static bool IsSupported => RenderCapability.Tier >> 16 > 0;

    internal static void Attach(Window window) => window.SourceInitialized += OnSourceInitialized;

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        var window = (Window)sender!;
        window.SourceInitialized -= OnSourceInitialized;
        var handle = new WindowInteropHelper(window).Handle;
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = PluginPalette.Transparent;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins);
        // Windows 11 would otherwise round the corners of a window that covers the whole monitor.
        var corners = DwmwcpDoNotRound;
        NativeMethods.DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corners, sizeof(int));
    }
}
