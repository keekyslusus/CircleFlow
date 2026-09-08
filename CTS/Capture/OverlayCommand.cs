using CircleToSearch.Search;
using CircleToSearch.MusicRecognition;
using CircleToSearch.TextRecognition;

namespace CircleToSearch.Capture;

public interface IOverlayCommand;

public sealed record ProviderSelected : IOverlayCommand
{
    public ProviderSelected(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId;
    }

    public string ProviderId { get; }
}

public sealed record VisualSelection : IOverlayCommand
{
    public VisualSelection(SelectionOutcome selection, string providerId)
    {
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId;
    }

    public SelectionOutcome Selection { get; }
    public string ProviderId { get; }
}

public sealed record StartMusicRecognition : IOverlayCommand;
public sealed record MusicDebugScenarioSelected(MusicDebugScenario Scenario) : IOverlayCommand;
public sealed record CancelSession : IOverlayCommand;
public sealed record DismissMusicResult : IOverlayCommand;
public sealed record RetryMusicRecognition : IOverlayCommand;
public sealed record OpenTraceResult : IOverlayCommand;
public sealed record OpenMusicResult : IOverlayCommand;
public sealed record CopyMusicResult : IOverlayCommand;

public sealed record SearchSelectedText : IOverlayCommand
{
    public SearchSelectedText(string text, string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        Text = text;
        ProviderId = providerId;
    }

    public string Text { get; }
    public string ProviderId { get; }
}

public sealed record ScreenTranslationRequested : IOverlayCommand
{
    public ScreenTranslationRequested(Guid requestId, System.Windows.Media.Imaging.BitmapSource image, string targetLanguageTag)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("A translation request id is required.", nameof(requestId));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguageTag);
        RequestId = requestId;
        Image = image ?? throw new ArgumentNullException(nameof(image));
        TargetLanguageTag = targetLanguageTag;
    }

    public ScreenTranslationRequested(Guid requestId, OcrDocument document, string targetLanguageTag)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("A translation request id is required.", nameof(requestId));
        RequestId = requestId;
        Document = document ?? throw new ArgumentNullException(nameof(document));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguageTag);
        TargetLanguageTag = targetLanguageTag;
    }

    public Guid RequestId { get; }
    public OcrDocument? Document { get; }
    public System.Windows.Media.Imaging.BitmapSource? Image { get; }
    public string TargetLanguageTag { get; }
}

public sealed record CancelScreenTranslation : IOverlayCommand
{
    public CancelScreenTranslation(Guid requestId)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("A translation request id is required.", nameof(requestId));
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}

public sealed record OverlayLaunchOptions
{
    public OverlayLaunchOptions(
        OverlayOptions captureOptions,
        Ui.UiStrings strings,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string initialProviderId)
    {
        CaptureOptions = captureOptions ?? throw new ArgumentNullException(nameof(captureOptions));
        Strings = strings ?? throw new ArgumentNullException(nameof(strings));
        Providers = providers ?? throw new ArgumentNullException(nameof(providers));
        ArgumentException.ThrowIfNullOrWhiteSpace(initialProviderId);
        if (!providers.Any(provider => string.Equals(provider.Id, initialProviderId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The initial provider must be present in the provider list.", nameof(initialProviderId));
        InitialProviderId = initialProviderId;
    }

    public OverlayOptions CaptureOptions { get; }
    public Ui.UiStrings Strings { get; }
    public IReadOnlyList<SearchProviderDescriptor> Providers { get; }
    public string InitialProviderId { get; }
}
