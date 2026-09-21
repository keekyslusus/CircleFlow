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
        Func<bool>? traceTheme = null,
        Action<System.Windows.Media.Imaging.BitmapSource>? setImageClipboard = null) =>
        new(context => CompositionRoot.CreateOverlayControllers(
            context,
            new CompositionRoot.OverlayControllerDependencies(
                setClipboard ?? Clipboard.SetText,
                animationsEnabled ?? OverlayVisualResources.AnimationsEnabled,
                pointerPosition,
                ocrRecognizer ?? DisabledOcrRecognizer.Instance,
                textHitToleranceDips,
                translationConsentAccepted ?? (() => true),
                acceptTranslationConsent ?? (() => { }),
                resetTranslationConsent,
                log,
                memoryProfiler,
                createTraceVideo,
                traceTheme ?? (() => context.Visual.LightTheme),
                SetImageClipboard: setImageClipboard)));
}
