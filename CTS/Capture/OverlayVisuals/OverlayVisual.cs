namespace CircleToSearch.Capture;

using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui.Effects;

public sealed record OverlayVisual(
    bool LightTheme,
    Grid Root,
    SelectionOverlayVisual Selection,
    ActionTrayVisual Actions,
    ProviderMenuVisual? Provider,
    MusicOverlayVisual Music,
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
    TranslateTransform Lift);

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

public sealed record ProviderMenuVisual(
    Button Button,
    ContentControl Content,
    Path Chevron,
    Border Menu);

public sealed record MusicOverlayVisual(
    Button Button,
    Path Icon,
    StackPanel ListeningLayer,
    AudioWaveformVisual Waveform,
    Grid ResultHost,
    Border DebugPanel,
    Panel DebugScenarioButtons,
    Panel DebugToastButtons);

public sealed record OverlayEffectsVisual(
    Canvas SceneRippleLayer,
    SceneRippleHost SceneRipples);
