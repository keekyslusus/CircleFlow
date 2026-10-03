using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using Xunit;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace CircleToSearch.Tests;

public sealed class OcrTranslationPreviewTests
{
    [SkippableFact]
    public void Renders_text_selection_and_translation_consent_previews()
    {
        TestSwitches.Require("CTS_OCR_TRANSLATION_PREVIEW");
        Assert.Null(RunOnSta(() =>
        {
            var directory = TestOutputPaths.TempDirectory;
            Directory.CreateDirectory(directory);
            var visual = OverlayVisualFactory.CreateRoot(null, new Size(800, 500), 28, false, TestUiStrings.English);
            visual.Root.Background = new LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(0x2D, 0x35, 0x44),
                System.Windows.Media.Color.FromRgb(0x73, 0x82, 0x96),
                25);
            var window = new Window { Width = 800, Height = 500, Content = visual.Root, WindowStyle = WindowStyle.None };
            window.Show();
            window.UpdateLayout();

            AddHighlight(visual.TextSelection.HighlightLayer, new Rect(120, 120, 160, 25));
            AddHighlight(visual.TextSelection.HighlightLayer, new Rect(120, 154, 260, 25));
            visual.TextSelection.Toolbar.Show(new Rect(120, 120, 260, 59), new Size(800, 500));
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Capture(visual.Root, Path.Combine(directory, "ocr-text-selection-preview.png"));

            visual.TextSelection.Toolbar.Hide(animate: false);
            visual.TextSelection.HighlightLayer.Children.Clear();
            var consent = StateCardVisualFactory.Create(new StateCardOptions(
                PluginIcons.TranslateFilled,
                TestUiStrings.English.TranslationConsentMessage,
                TestUiStrings.English.TranslationConsentTitle,
                TestUiStrings.English.ConsentCancel,
                () => { },
                new StateCardAction(TestUiStrings.English.Continue, () => { }),
                TestUiStrings.English.TranslationConsentTitle), PluginPalette.For(false).Card);
            visual.TranslationOverlay.StateHost.Children.Add(consent.Card);
            visual.TranslationOverlay.StateHost.Visibility = Visibility.Visible;
            visual.Bottom.Stack.Children.Insert(0, visual.TranslationOverlay.StateHost);
            Capture(visual.Root, Path.Combine(directory, "ocr-translation-consent-preview.png"));

            visual.Music.Waveform.Dispose();
            visual.TranslationAction.LoadingIndicator.Dispose();
            visual.Effects.SceneRipples.Dispose();
            window.Close();
        }));
    }

    private static void AddHighlight(Canvas canvas, Rect bounds)
    {
        var rectangle = new Rectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            RadiusX = 2,
            RadiusY = 2,
            Fill = OverlayVisualResources.Frozen(PluginPalette.For(false).TextInteraction.Selection),
        };
        Canvas.SetLeft(rectangle, bounds.Left);
        Canvas.SetTop(rectangle, bounds.Top);
        canvas.Children.Add(rectangle);
    }

    private static void Capture(UIElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1200, 750, 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive);
        return failure;
    }
}
