using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;
using CircleToSearch.Interop;
using CircleToSearch.Search.Browser;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

// Opt-in and slow (about 35 minutes for every language): sends one screenshot per target language to the live
// Google image translation, spaced out so the run stays under its rate limit.
//   dotnet test --filter "FullyQualifiedName~GoogleImageTranslationLanguagesLiveTests" -e CTS_LIVE=1 -e CTS_WEBVIEW2_LIVE=1
// CTS_TRANSLATION_LANGUAGES=ace,btx checks only those codes; CTS_TRANSLATION_PACE_SECONDS changes the spacing.
// Results go to tests/temp/translation-languages as they arrive, so an interrupted run still shows what it checked.
public sealed class GoogleImageTranslationLanguagesLiveTests
{
    private static readonly TimeSpan DefaultPace = TimeSpan.FromSeconds(11);
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Every_listed_language_translates_a_screenshot()
    {
        TestSwitches.Require("CTS_LIVE", "CTS_WEBVIEW2_LIVE");
        var codes = Codes();
        var pace = Pace();
        var output = Path.Combine(TestOutputPaths.TempDirectory, "translation-languages");
        Directory.CreateDirectory(output);
        var report = Path.Combine(output, "report.tsv");
        await File.WriteAllTextAsync(report, "code\tresult\tdetail" + Environment.NewLine);

        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "CircleFlow.TranslationLanguagesLive"));
        AppDataDirectory.Initialize(paths);
        var environments = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        using var http = new HttpClient(new SocketsHttpHandler
        { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        await using var signer = new GoogleImageTranslationSigner(http,
            new StaDispatcher("CircleToSearch translation languages live test"),
            () => environments.CreateAsync(paths.ImageTranslationProfileDirectory));
        var provider = new GoogleImageTranslationProvider(http, signer);
        var screenshot = SampleScreenshot();
        await File.WriteAllBytesAsync(Path.Combine(output, "source.png"), Png(screenshot));

        var failures = new List<string>();
        var clock = new Stopwatch();
        foreach (var code in codes)
        {
            var (result, detail) = await TranslateAsync(provider, screenshot, code, output, pace, clock);
            if (result == "rate-limited")
            {
                await Task.Delay(RateLimitPause);
                (result, detail) = await TranslateAsync(provider, screenshot, code, output, pace, clock);
            }
            await File.AppendAllTextAsync(report, $"{code}\t{result}\t{detail}{Environment.NewLine}");
            if (result != "translated") failures.Add($"{code}: {result} {detail}");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {codes.Count} languages failed, see {report}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    // Requests start one pace apart, however long the previous one took.
    private static async Task<(string Result, string Detail)> TranslateAsync(GoogleImageTranslationProvider provider,
        BitmapSource screenshot, string code, string output, TimeSpan pace, Stopwatch clock)
    {
        var wait = pace - clock.Elapsed;
        if (clock.IsRunning && wait > TimeSpan.Zero) await Task.Delay(wait);
        clock.Restart();
        using var timeout = new CancellationTokenSource(RequestTimeout);
        try
        {
            var translated = await provider.TranslateAsync(screenshot, code, timeout.Token);
            await File.WriteAllBytesAsync(Path.Combine(output, code + ".png"), Png(translated));
            return ("translated", string.Empty);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return ("rate-limited", error.Message);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return ("timeout", string.Empty);
        }
        catch (Exception error)
        {
            return ("failed", $"{error.GetType().Name}: {string.Join(' ', error.Message.Split(['\r', '\n', '\t']))}");
        }
    }

    private static IReadOnlyList<string> Codes()
    {
        var chosen = Environment.GetEnvironmentVariable("CTS_TRANSLATION_LANGUAGES");
        if (string.IsNullOrWhiteSpace(chosen)) return TranslationLanguages.AllCodes;
        return chosen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => TranslationLanguages.Find(code) ?? throw new ArgumentException($"{code} is not in the list."))
            .ToArray();
    }

    private static TimeSpan Pace() =>
        double.TryParse(Environment.GetEnvironmentVariable("CTS_TRANSLATION_PACE_SECONDS"),
            System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultPace;

    private static BitmapSource SampleScreenshot()
    {
        using var bitmap = new Bitmap(900, 260);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var font = new Font("Segoe UI", 30);
            graphics.DrawString("Good morning! The weather is nice today.", font, Brushes.Black, 30, 40);
            graphics.DrawString("Where is the nearest train station?", font, Brushes.Black, 30, 140);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    private static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
