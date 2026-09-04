using System.Drawing;

namespace CircleToSearch.TextRecognition;

public sealed record OcrFrameTile(Rectangle BoundsPx, Rectangle CoreBoundsPx)
{
    public bool Owns(Rectangle wordBounds)
    {
        var centerX = wordBounds.Left + wordBounds.Width / 2d;
        var centerY = wordBounds.Top + wordBounds.Height / 2d;
        return centerX >= CoreBoundsPx.Left && centerX < CoreBoundsPx.Right &&
               centerY >= CoreBoundsPx.Top && centerY < CoreBoundsPx.Bottom;
    }
}

public sealed class OcrFrameTiler(int overlapPx = 64)
{
    public int OverlapPx { get; } = overlapPx >= 0
        ? overlapPx
        : throw new ArgumentOutOfRangeException(nameof(overlapPx));

    public IReadOnlyList<OcrFrameTile> Create(int width, int height, int maximumDimension)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (maximumDimension <= OverlapPx) throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        var horizontal = CreateAxis(width, maximumDimension);
        var vertical = CreateAxis(height, maximumDimension);
        var tiles = new List<OcrFrameTile>(horizontal.Count * vertical.Count);
        foreach (var y in vertical)
        foreach (var x in horizontal)
            tiles.Add(new OcrFrameTile(
                new Rectangle(x.Start, y.Start, x.Length, y.Length),
                Rectangle.FromLTRB(x.CoreStart, y.CoreStart, x.CoreEnd, y.CoreEnd)));
        return tiles;
    }

    private IReadOnlyList<AxisTile> CreateAxis(int length, int maximumDimension)
    {
        if (length <= maximumDimension) return [new AxisTile(0, length, 0, length)];
        var starts = new List<int>();
        var step = maximumDimension - OverlapPx;
        for (var start = 0; ; start += step)
        {
            starts.Add(start);
            if (start + maximumDimension >= length) break;
        }
        var tiles = new List<AxisTile>(starts.Count);
        for (var index = 0; index < starts.Count; index++)
        {
            var start = starts[index];
            var end = Math.Min(length, start + maximumDimension);
            var coreStart = index == 0 ? 0 : (start + Math.Min(length, starts[index - 1] + maximumDimension)) / 2;
            var coreEnd = index == starts.Count - 1 ? length : (starts[index + 1] + end) / 2;
            tiles.Add(new AxisTile(start, end - start, coreStart, coreEnd));
        }
        return tiles;
    }

    private sealed record AxisTile(int Start, int Length, int CoreStart, int CoreEnd);
}
