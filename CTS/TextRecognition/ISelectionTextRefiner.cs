using System.Windows.Media.Imaging;

namespace CircleToSearch.TextRecognition;

public interface ISelectionTextRefiner
{
    Task<SelectionTextRefinement> RefineAsync(
        BitmapSource frozenFrame,
        TextSelectionRange selection,
        CancellationToken cancellationToken);
}

public enum SelectionTextRefinementStatus
{
    Success,
    NoText,
    Canceled,
    Failed,
}

public sealed record SelectionTextRefinement(SelectionTextRefinementStatus Status, string? Text)
{
    public static SelectionTextRefinement Success(string text) =>
        new(SelectionTextRefinementStatus.Success, string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("Refined text is required.", nameof(text))
            : text);
    public static SelectionTextRefinement NoText() => new(SelectionTextRefinementStatus.NoText, null);
    public static SelectionTextRefinement Canceled() => new(SelectionTextRefinementStatus.Canceled, null);
    public static SelectionTextRefinement Failed() => new(SelectionTextRefinementStatus.Failed, null);
}

internal sealed class DisabledSelectionTextRefiner : ISelectionTextRefiner
{
    internal static DisabledSelectionTextRefiner Instance { get; } = new();

    public Task<SelectionTextRefinement> RefineAsync(
        BitmapSource frozenFrame,
        TextSelectionRange selection,
        CancellationToken cancellationToken) => Task.FromResult(SelectionTextRefinement.NoText());
}
