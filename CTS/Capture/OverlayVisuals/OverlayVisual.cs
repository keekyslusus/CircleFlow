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
    Grid ActivityHost,
    ProviderMenuVisual? Provider,
    MusicOverlayVisual Music,
    DebugOverlayVisual Debug,
    BottomOverlayVisual Bottom,
    OverlayEffectsVisual Effects,
    ImageSelectionVisual ImageSelection,
    QrCodeVisual QrCodes);

public sealed record QrCodeVisual(
    Grid Layer,
    Canvas Viewfinders,
    bool LightTheme,
    Color Accent,
    System.Windows.Thickness ToolbarSafeInsets);

public sealed record ImageSelectionVisual(
    FloatingToolbar Toolbar,
    Button SearchButton,
    Button CopyButton,
    Button SaveButton,
    Button TranslateButton,
    Button AskButton,
    FloatingToolbarPrompt AskPrompt);

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
    public SelectionHintVisual? Hint { get; init; }
}

public sealed record SelectionHintVisual(
    StackPanel Content,
    StackPanel Keys,
    TextBlock Action,
    Brush KeycapBackground,
    Brush KeycapBorder,
    Brush KeycapText);

public sealed record TextSelectionVisual(
    Canvas HighlightLayer,
    FloatingToolbar Toolbar,
    Button CopyButton,
    Button SearchButton,
    Button OpenLinkButton);

public sealed record TranslationActionVisual(
    Button Button,
    Path Icon,
    LoadingIndicatorVisual LoadingIndicator);

public sealed record TranslationOverlayVisual(Grid StateHost);

public sealed record BottomOverlayVisual(
    Grid Root,
    StackPanel Stack,
    Grid ResultSlot,
    Grid ActionSlot,
    Canvas ProviderMenuLayer,
    StackLayoutTransitions LayoutTransitions,
    StackLayoutTransitions TrayTransitions);

internal sealed record ToastOverlayVisual(
    Grid Slot,
    Border Card,
    TextBlock Message);

internal sealed record StateCardVisual(
    Border Card,
    Path Icon,
    TextBlock Message,
    Button CloseButton,
    Button? PrimaryActionButton,
    TextBlock? Title);

public sealed record ProviderMenuVisual(
    Button Button,
    ContentControl Content,
    Path Chevron,
    Border Menu);

public sealed record MusicOverlayVisual(
    Button Button,
    Path Icon,
    LoadingIndicatorVisual LoadingIndicator,
    AudioWaveformVisual Waveform,
    Grid ResultHost);

public sealed record DebugOverlayVisual(
    Border Panel,
    Panel MusicScenarioButtons,
    Panel ToastButtons,
    Button ResetTranslationConsentButton);

public sealed record OverlayEffectsVisual(
    Canvas SceneRippleLayer,
    SceneRippleHost SceneRipples);
