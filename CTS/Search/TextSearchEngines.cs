using CircleToSearch.Interop;

namespace CircleToSearch.Search;

public sealed record TextSearchEngine(string Id, string UrlTemplate, bool NeedsSignIn = false);

public static class TextSearchEngines
{
    public const string MatchImageSearch = "";
    public const string Bing = "bing";
    public const string DuckDuckGo = "duckduckgo";
    public const string Google = "google";
    public const string Kagi = "kagi";
    public const string Qwant = "qwant";
    public const string Startpage = "startpage";
    public const string QueryPlaceholder = "%s";

    public static IReadOnlyList<TextSearchEngine> All { get; } =
    [
        new(Bing, "https://www.bing.com/search?q=%s"),
        new(DuckDuckGo, "https://duckduckgo.com/?q=%s"),
        new(Google, "https://www.google.com/search?q=%s"),
        new(Kagi, "https://kagi.com/search?q=%s", NeedsSignIn: true),
        new(Qwant, "https://www.qwant.com/?q=%s"),
        new(Startpage, "https://www.startpage.com/sp/search?q=%s"),
    ];

    public static TextSearchEngine? Find(string? id) =>
        All.FirstOrDefault(engine => string.Equals(engine.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    // Cleaning the built-in browser's data signs the user out, so a paid engine would keep asking to sign in again.
    public static bool IsAvailable(string? id, bool builtInBrowser, int browserDataCleanupDays) =>
        !builtInBrowser || browserDataCleanupDays == BrowserDataCleanup.Never || Find(id) is not { NeedsSignIn: true };
}
