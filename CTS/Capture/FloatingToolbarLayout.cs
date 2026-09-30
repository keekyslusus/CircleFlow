using System.Windows;

namespace CircleToSearch.Capture;

public static class FloatingToolbarLayout
{
    private const double EdgeMargin = 16;
    // Shared with the bottom chips: a toolbar inside a full-screen selection sits on their row, where actions
    // usually appear, instead of blending into the taskbar.
    internal const double BottomActionsMargin = 32;

    public static double AvailableWidth(double viewportWidth, Thickness safeInsets) =>
        Math.Max(0, viewportWidth - safeInsets.Left - safeInsets.Right - 2 * EdgeMargin);

    public static Point Place(Rect selection, Size toolbar, Size viewport, Thickness safeInsets = default, double gap = 8,
        bool preferBelow = false)
    {
        var safeTop = safeInsets.Top;
        var safeBottom = viewport.Height - safeInsets.Bottom;
        var x = selection.Left + (selection.Width - toolbar.Width) / 2;
        var above = selection.Top - gap - toolbar.Height;
        var below = selection.Bottom + gap;
        var fitsAbove = above >= safeTop + EdgeMargin;
        var fitsBelow = below + toolbar.Height <= safeBottom - EdgeMargin;
        var y = fitsBelow && (preferBelow || !fitsAbove) ? below
            : fitsAbove ? above
            : Math.Min(selection.Bottom, safeBottom) - BottomActionsMargin - toolbar.Height;
        return new Point(
            Clamp(x, toolbar.Width, safeInsets.Left, viewport.Width - safeInsets.Right, viewport.Width),
            Clamp(y, toolbar.Height, safeTop, safeBottom, viewport.Height));
    }

    private static double Clamp(double value, double size, double start, double end, double extent)
    {
        var min = start + EdgeMargin;
        var max = end - EdgeMargin - size;
        return max < min ? Math.Max(0, (extent - size) / 2) : Math.Clamp(value, min, max);
    }
}
