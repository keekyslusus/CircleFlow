namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Media;

public static class SelectionOverlayTransitions
{
    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(150);

    public static Geometry BuildRevealGeometry(Size size, IReadOnlyList<Point> polygon)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            context.BeginFigure(
                new Point(-SelectionOverlayVisualFactory.RevealBleed, -SelectionOverlayVisualFactory.RevealBleed),
                true,
                true);
            context.PolyLineTo(
                [
                    new Point(
                        size.Width + SelectionOverlayVisualFactory.RevealBleed,
                        -SelectionOverlayVisualFactory.RevealBleed),
                    new Point(
                        size.Width + SelectionOverlayVisualFactory.RevealBleed,
                        size.Height + SelectionOverlayVisualFactory.RevealBleed),
                    new Point(
                        -SelectionOverlayVisualFactory.RevealBleed,
                        size.Height + SelectionOverlayVisualFactory.RevealBleed),
                ],
                true,
                true);
            if (polygon.Count >= 2)
            {
                var rest = new Point[polygon.Count - 1];
                for (var i = 1; i < polygon.Count; i++) rest[i - 1] = polygon[i];
                context.BeginFigure(polygon[0], true, true);
                context.PolyLineTo(rest, true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    public static Geometry BuildPolygonGeometry(IReadOnlyList<Point> polygon)
    {
        if (polygon.Count < 2) return Geometry.Empty;
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            var rest = new Point[polygon.Count - 1];
            for (var i = 1; i < polygon.Count; i++) rest[i - 1] = polygon[i];
            context.BeginFigure(polygon[0], true, true);
            context.PolyLineTo(rest, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    public static Geometry BuildSelectionFrameGeometry(Rect rect)
    {
        var geometry = new RectangleGeometry(
            rect,
            SelectionOverlayVisualFactory.FrameCornerRadius,
            SelectionOverlayVisualFactory.FrameCornerRadius);
        geometry.Freeze();
        return geometry;
    }

    public static void BeginSelectionReveal(
        SelectionOverlayVisual visual,
        Geometry revealGeometry,
        Geometry frameGeometry)
    {
        visual.DimRect.Data = revealGeometry;
        visual.SelectionFrame.Data = frameGeometry;
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            visual.Dim.Opacity = 0;
            visual.Sheen.Opacity = 0;
            visual.Halo.Opacity = 0;
            visual.Accent.Opacity = 0;
            visual.DimRect.Opacity = 1;
            visual.SelectionFrame.Opacity = 1;
            return;
        }

        visual.Dim.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(1, 0, RevealDuration));
        visual.Sheen.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(1, 0, RevealDuration));
        visual.Halo.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(1, 0, RevealDuration));
        visual.Accent.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(1, 0, RevealDuration));
        visual.DimRect.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(0, 1, RevealDuration));
        visual.SelectionFrame.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(0, 1, RevealDuration));
    }
}
