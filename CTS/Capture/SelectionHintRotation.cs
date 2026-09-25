namespace CircleToSearch.Capture;

// Ordered by how hard each gesture is to discover on its own, so the first overlay after launch teaches right-drag.
public sealed class SelectionHintRotation
{
    private static readonly SelectionHint[] Hints =
        [SelectionHint.RightDragActions, SelectionHint.LeftDragSearch, SelectionHint.EscapeCancel];

    private int _next = -1;

    public SelectionHint Next() => Hints[(int)((uint)Interlocked.Increment(ref _next) % (uint)Hints.Length)];
}
