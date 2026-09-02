namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using CircleToSearch.Ocr;
using CircleToSearch.Ui;

public sealed class TextSelectionVisual
{
    public Canvas HighlightLayer { get; }

    public TextSelectionVisual()
    {
        HighlightLayer = new Canvas { IsHitTestVisible = false };
    }

    public void UpdateSelection(IReadOnlyList<OcrWordSnapshot> selectedWords)
    {
        HighlightLayer.Children.Clear();
        if (selectedWords.Count == 0) return;

        var highlightBrush = OverlayVisualResources.Frozen(PluginPalette.TextSelectionHighlight);
        foreach (var word in selectedWords)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(0, word.DipRect.Width),
                Height = Math.Max(0, word.DipRect.Height),
                Fill = highlightBrush,
                RadiusX = 3,
                RadiusY = 3,
            };
            Canvas.SetLeft(rect, word.DipRect.X);
            Canvas.SetTop(rect, word.DipRect.Y);
            HighlightLayer.Children.Add(rect);
        }
    }

    public void Clear()
    {
        HighlightLayer.Children.Clear();
    }
}
