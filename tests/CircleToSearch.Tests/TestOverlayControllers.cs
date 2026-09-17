using System.Windows;
using System.Windows.Input;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;

namespace CircleToSearch.Tests;

internal static class TestOverlayControllers
{
    internal static OverlayControllerFactory CreateFactory(
        Action<string>? setClipboard = null,
        Func<bool>? animationsEnabled = null,
        Func<MouseEventArgs, Point>? pointerPosition = null,
        IOcrRecognizer? ocrRecognizer = null,
        double textHitToleranceDips = 3,
        Func<bool>? translationConsentAccepted = null,
        Action? acceptTranslationConsent = null,
        Action? resetTranslationConsent = null,
        PluginLog? log = null,
        TranslationMemoryProfiler? memoryProfiler = null,
        Func<Uri, ITraceVideoPreview>? createTraceVideo = null,
        Func<bool>? traceTheme = null) =>
        new(
            (context, clipboardCopy) => new TraceOverlayController(
                context.Visual.Root,
                context.Visual.Bottom,
                context.Visual.Effects,
                context.Strings,
                traceTheme ?? (() => context.Visual.LightTheme),
                clipboardCopy,
                context.GetMode,
                context.TransitionMode,
                context.PublishCommand,
                context.CreateSelectionCopy,
                createTraceVideo),
            context => new ActionTrayOverlayController(
                context.Visual.Actions,
                context.Visual.Bottom.Root),
            setClipboard ?? Clipboard.SetText,
            animationsEnabled ?? OverlayVisualResources.AnimationsEnabled,
            pointerPosition,
            ocrRecognizer,
            textHitToleranceDips,
            translationConsentAccepted,
            acceptTranslationConsent,
            resetTranslationConsent,
            log,
            memoryProfiler);
}
