namespace CircleToSearch.Translation;

using System.Windows;
using System.Windows.Media;

public sealed record TranslationBlock(
    Rect DipBounds,
    string OriginalText,
    string TranslatedText,
    double FontSize,
    Color BackgroundColor,
    Color TextColor,
    FontWeight FontWeight);
