namespace CircleToSearch.Ocr;

using System.Windows;
using GdiRectangle = System.Drawing.Rectangle;

public sealed record OcrWordSnapshot(
    string Text,
    Rect DipRect,
    GdiRectangle PixelRect);
