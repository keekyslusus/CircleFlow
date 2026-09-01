using System.Drawing;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SelectionGestureClassifierTests
{
    [Fact]
    public void Empty_path_is_too_small() =>
        Assert.Equal(
            SelectionGestureKind.TooSmall,
            SelectionGestureClassifier.Classify([], 12, 3));

    [Fact]
    public void Exact_click_is_pixel_pick() =>
        Assert.Equal(
            SelectionGestureKind.PixelPick,
            SelectionGestureClassifier.Classify([new Point(10, 20)], 12, 3));

    [Fact]
    public void Three_pixel_diagonal_is_pixel_pick() =>
        Assert.Equal(
            SelectionGestureKind.PixelPick,
            SelectionGestureClassifier.Classify([new Point(0, 0), new Point(3, 0)], 12, 3));

    [Fact]
    public void More_than_three_but_less_than_minimum_is_too_small() =>
        Assert.Equal(
            SelectionGestureKind.TooSmall,
            SelectionGestureClassifier.Classify([new Point(0, 0), new Point(4, 0)], 12, 3));

    [Fact]
    public void Exact_minimum_is_visual_selection() =>
        Assert.Equal(
            SelectionGestureKind.VisualSelection,
            SelectionGestureClassifier.Classify([new Point(0, 0), new Point(12, 0)], 12, 3));

    [Fact]
    public void Large_closed_path_is_not_mistaken_for_click() =>
        Assert.Equal(
            SelectionGestureKind.VisualSelection,
            SelectionGestureClassifier.Classify(
                [new Point(0, 0), new Point(40, 0), new Point(40, 30), new Point(0, 0)],
                12,
                3));

    [Fact]
    public void Pixel_pick_has_priority_when_selection_minimum_is_lower() =>
        Assert.Equal(
            SelectionGestureKind.PixelPick,
            SelectionGestureClassifier.Classify([new Point(0, 0), new Point(2, 0)], 1, 3));
}
