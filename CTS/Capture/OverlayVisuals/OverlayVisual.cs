namespace CircleToSearch.Capture;

using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui.Effects;

public sealed record OverlayVisual(
    bool LightTheme,
    Grid Root,
    SelectionOverlayVisual Selection,
    TextSelectionVisual TextSelection,
    ActionTrayVisual Actions,
    TranslationActionVisual TranslationAction,
    TranslationOverlayVisual TranslationOverlay,
    ProviderMenuVisual? Provider,
    MusicOverlayVisual Music,
    DebugOverlayVisual Debug,
    BottomOverlayVisual Bottom,
    OverlayEffectsVisual Effects);

public sealed record SelectionOverlayVisual(
    Image Screenshot,
    Path Dim,
    Path DimRect,
    Path Sheen,
    Polyline Halo,
    Polyline Accent,
    Path SelectionFrame,
    Grid InputSurface);

public sealed record ActionTrayVisual(
    StackPanel Tray,
    Border Chip,
    TranslateTransform Lift)
{
    public TextBlock? Prompt { get; init; }
}

public sealed record TextSelectionVisual(
    Canvas HighlightLayer,
    Canvas ActionLayer,
    Border ActionCard,
    Button CopyButton,
    Button SearchButton);

public sealed record TranslationActionVisual(
    Button Button,
    Path Icon,
    LoadingIndicatorVisual LoadingIndicator);

public sealed record TranslationOverlayVisual(
    Border ConsentCard,
    Button ContinueButton,
    Button CancelButton);

public sealed record BottomOverlayVisual(
    Grid Root,
    StackPanel Stack,
    Grid ResultSlot,
    Grid ActionSlot,
    Canvas ProviderMenuLayer,
    BottomOverlayLayoutTransitions LayoutTransitions);

internal sealed record ToastOverlayVisual(
    Grid Slot,
    Border Card,
    TextBlock Message);

internal sealed record ResultStateCardVisual(
    Border Card,
    Path Icon,
    TextBlock Message,
    Button CloseButton,
    Button? PrimaryActionButton);

public sealed record ProviderMenuVisual(
    Button Button,
    ContentControl Content,
    Path Chevron,
    Border Menu);

public sealed record MusicOverlayVisual(
    Button Button,
    Path Icon,
    LoadingIndicatorVisual LoadingIndicator,
    StackPanel ListeningLayer,
    AudioWaveformVisual Waveform,
    Grid ResultHost);

public sealed record DebugOverlayVisual(
    Border Panel,
    Panel MusicScenarioButtons,
    Panel ToastButtons);

public sealed record OverlayEffectsVisual(
    Canvas SceneRippleLayer,
    SceneRippleHost SceneRipples);
