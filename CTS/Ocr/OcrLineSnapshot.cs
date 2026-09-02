namespace CircleToSearch.Ocr;

using System.Windows;
using GdiRectangle = System.Drawing.Rectangle;

public sealed record OcrLineSnapshot(
    string Text,
    Rect DipRect,
    GdiRectangle PixelRect,
    IReadOnlyList<OcrWordSnapshot> Words);
