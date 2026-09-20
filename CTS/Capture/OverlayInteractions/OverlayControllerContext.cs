using System.Windows;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.Interop;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed record OverlayControllerContext(
    OverlayVisual Visual,
    FrameworkElement CoordinateRoot,
    GdiRectangle Monitor,
    double Scale,
    OverlayOptions Options,
    bool Overscan,
    IReadOnlyList<SearchProviderDescriptor> Providers,
    string SelectedProviderId,
    UiStrings Strings,
    Action<IOverlayCommand>? PublishCommand,
    Func<GdiRectangle, SelectionOutcome> CreateSelectionCopy,
    Func<bool> CanAcceptSelectionInput,
    Func<bool> CanAcceptPointerInput,
    Func<object?, Point, bool> CanStartSelection,
    Action SelectionStarted,
    Action<GdiRectangle> SelectionCompleted,
    Action SelectionRejected,
    Action SelectionHoldCompleted,
    Func<bool> CanUseProvider,
    Action<string> ProviderSelected,
    Func<OverlayInteractionMode> GetMode,
    Action MusicStartRequested,
    Action MusicCancelRequested,
    Action<MusicDebugScenario> DebugScenarioSelected,
    Action<IOverlayCommand> MusicResultCommandRequested,
    Action<OverlayInteractionMode> TransitionMode,
    string? OcrLanguageTag = null,
    string TranslationTargetLanguageTag = "en",
    KeyboardLanguageSnapshot InputLanguage = default);
