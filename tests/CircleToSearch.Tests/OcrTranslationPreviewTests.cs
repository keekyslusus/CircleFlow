using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using Xunit;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace CircleToSearch.Tests;

public sealed class OcrTranslationPreviewTests
{
    [Fact]
    public void Renders_text_consent_and_translation_previews()
    {
        if (Environment.GetEnvironmentVariable("CTS_OCR_TRANSLATION_PREVIEW") != "1") return;
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
            visual.TextSelection.ActionCard.Visibility = Visibility.Visible;
            Canvas.SetLeft(visual.TextSelection.ActionCard, 170);
            Canvas.SetTop(visual.TextSelection.ActionCard, 72);
            Capture(visual.Root, Path.Combine(directory, "ocr-text-selection-preview.png"));

            visual.TextSelection.ActionCard.Visibility = Visibility.Collapsed;
            visual.TextSelection.HighlightLayer.Children.Clear();
            visual.TranslationOverlay.ConsentCard.Visibility = Visibility.Visible;
            Capture(visual.Root, Path.Combine(directory, "ocr-translation-consent-preview.png"));

            visual.TranslationOverlay.ConsentCard.Visibility = Visibility.Collapsed;
            AddTranslationCard(visual.TranslationOverlay.CardsLayer, "Hello from the translated screen", 120, 120, 300);
            AddTranslationCard(visual.TranslationOverlay.CardsLayer, "A longer translated line wraps without clipping.", 120, 178, 360);
            visual.TranslationOverlay.CardsLayer.Visibility = Visibility.Visible;
            Capture(visual.Root, Path.Combine(directory, "ocr-translation-cards-preview.png"));

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

    private static void AddTranslationCard(Canvas canvas, string text, double left, double top, double width)
    {
        var palette = PluginPalette.For(false).Translation;
        var card = new Border
        {
            Width = width,
            Background = OverlayVisualResources.Frozen(palette.CardSurface),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 5, 8, 5),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = OverlayVisualResources.Frozen(palette.CardText),
                FontFamily = OverlayVisualResources.Font,
                FontSize = 15,
            },
        };
        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
        canvas.Children.Add(card);
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
