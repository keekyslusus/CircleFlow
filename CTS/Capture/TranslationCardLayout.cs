using System.Windows;

namespace CircleToSearch.Capture;

public static class TranslationCardLayout
{
    public static IReadOnlyList<Rect> Place(
        IReadOnlyList<(Rect Source, Size Desired)> cards,
        Size viewport,
        double margin = 8,
        double gap = 4)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0) return [];
        var placements = new List<Rect>(cards.Count);
        var nextTop = margin;
        foreach (var card in cards.OrderBy(card => card.Source.Top).ThenBy(card => card.Source.Left))
        {
            var width = Math.Min(card.Desired.Width, Math.Max(0, viewport.Width - margin * 2));
            var height = card.Desired.Height;
            var left = Math.Clamp(card.Source.Left, margin, Math.Max(margin, viewport.Width - margin - width));
            var top = Math.Max(Math.Clamp(card.Source.Top, margin, Math.Max(margin, viewport.Height - margin - height)), nextTop);
            placements.Add(new Rect(left, top, width, height));
            nextTop = top + height + gap;
        }
        if (placements.Count == 0) return placements;
        var overflow = placements[^1].Bottom - (viewport.Height - margin);
        if (overflow <= 0) return placements;
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            placement.Y -= overflow;
            placements[index] = placement;
        }
        if (placements[0].Top >= margin) return placements;
        nextTop = margin;
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            placement.Y = nextTop;
            placements[index] = placement;
            nextTop = placement.Bottom + gap;
        }
        return placements;
    }
}
