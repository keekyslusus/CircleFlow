using System.Windows;

namespace CircleToSearch.Capture;

public static class TextActionCardLayout
{
    public static Point Place(Rect selection, Size card, Size viewport, double gap = 8)
    {
        var x = selection.Left + (selection.Width - card.Width) / 2;
        var y = selection.Top - card.Height - gap;
        if (y < 0) y = selection.Bottom + gap;
        x = Math.Clamp(x, 0, Math.Max(0, viewport.Width - card.Width));
        y = Math.Clamp(y, 0, Math.Max(0, viewport.Height - card.Height));
        return new Point(x, y);
    }
}
