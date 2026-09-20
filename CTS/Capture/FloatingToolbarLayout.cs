using System.Windows;

namespace CircleToSearch.Capture;

public static class FloatingToolbarLayout
{
    public static Point Place(Rect selection, Size toolbar, Size viewport, double gap = 8)
    {
        var x = selection.Left + (selection.Width - toolbar.Width) / 2;
        var y = selection.Top - toolbar.Height - gap;
        if (y < 0) y = selection.Bottom + gap;
        x = Math.Clamp(x, 0, Math.Max(0, viewport.Width - toolbar.Width));
        y = Math.Clamp(y, 0, Math.Max(0, viewport.Height - toolbar.Height));
        return new Point(x, y);
    }
}
