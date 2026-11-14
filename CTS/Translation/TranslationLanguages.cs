using System.Globalization;
using CircleToSearch.Ui;
using Windows.Globalization;

namespace CircleToSearch.Translation;

internal sealed record TranslationLanguageOption(string Code, string DisplayName);

// Target languages from https://cloud.google.com/translate/docs/languages, with the codes translate.google.com uses.
internal static class TranslationLanguages
{
    private static readonly string[] Codes =
    [
        "ab", "ace", "ach", "af", "sq", "alz", "am", "ar", "hy", "as", "awa", "ay", "az", "ban", "bm", "ba", "eu",
        "btx", "bts", "bbc", "be", "bem", "bn", "bew", "bho", "bik", "bs", "br", "bg", "bua", "yue", "ca", "ceb",
        "ny", "zh-CN", "zh-TW", "cv", "co", "crh", "hr", "cs", "da", "din", "dv", "doi", "dov", "nl", "dz", "en",
        "eo", "et", "ee", "fj", "tl", "fi", "fr", "fr-CA", "fy", "ff", "gaa", "gl", "lg", "ka", "de", "el", "gn",
        "gu", "ht", "cnh", "ha", "haw", "iw", "hil", "hi", "hmn", "hu", "hrx", "is", "ig", "ilo", "id", "ga", "it",
        "ja", "jw", "kn", "pam", "kk", "km", "cgg", "rw", "ktu", "gom", "ko", "kri", "ku", "ckb", "ky", "lo", "ltg",
        "la", "lv", "lij", "li", "ln", "lt", "lmo", "luo", "lb", "mk", "mai", "mak", "mg", "ms", "ms-Arab", "ml",
        "mt", "mi", "mr", "chm", "mni-Mtei", "min", "lus", "mn", "my", "nr", "new", "ne", "nso", "no", "nus", "oc",
        "or", "om", "pag", "pap", "ps", "fa", "pl", "pt", "pt-PT", "pa", "pa-Arab", "qu", "rom", "ro", "rn", "ru",
        "sm", "sg", "sa", "gd", "sr", "st", "crs", "shn", "sn", "scn", "szl", "sd", "si", "sk", "sl", "so", "es",
        "su", "sw", "ss", "sv", "tg", "ta", "tt", "te", "tet", "th", "ti", "ts", "tn", "tr", "tk", "ak", "uk", "ur",
        "ug", "uz", "vi", "cy", "xh", "yi", "yo", "yua", "zu",
    ];

    // Windows calls "ku" Central Kurdish, which Google lists separately as "ckb"; Google's "ku" is Kurmanji.
    private static readonly string[] MisnamedByWindows = ["ku"];

    // Google keeps legacy codes and writes Serbian in Cyrillic; Windows text recognition uses the standard tags.
    private static readonly Dictionary<string, string> RecognitionTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["no"] = "nb", ["sr"] = "sr-Cyrl", ["iw"] = "he", ["jw"] = "jv", ["tl"] = "fil",
    };

    // The Windows display language cannot change while the app runs, so each name is asked for once.
    private static readonly Lazy<IReadOnlyDictionary<string, string?>> WindowsNames = new(() =>
        Codes.ToDictionary(code => code, LookUpWindowsName, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> AllCodes { get; } = Array.AsReadOnly(Codes);

    public static string RecognitionTag(string code) => RecognitionTags.GetValueOrDefault(code.Trim(), code);

    public static string? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        return Array.Find(Codes, candidate => candidate.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<TranslationLanguageOption> Options(UiStrings strings, CultureInfo culture) =>
        Codes.Select(code => new TranslationLanguageOption(code, DisplayName(code, strings)))
            .OrderBy(option => option.DisplayName, StringComparer.Create(culture, ignoreCase: true))
            .ToArray();

    // Windows names languages in the Windows display language; the English list covers the ones it cannot name.
    public static string DisplayName(string code, UiStrings strings) =>
        WindowsName(code) ?? (Find(code) is { } listed ? strings.TranslationLanguageName(listed) : CultureName(code));

    private static string CultureName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).DisplayName; }
        catch (CultureNotFoundException) { return code; }
    }

    private static string? WindowsName(string code) =>
        WindowsNames.Value.TryGetValue(code, out var name) ? name : LookUpWindowsName(code);

    // For a code it does not know, Windows returns the code itself or wraps it, as in "Malay (ms-Arab)".
    private static string? LookUpWindowsName(string code)
    {
        if (MisnamedByWindows.Contains(code)) return null;
        try
        {
            var name = new Language(code).DisplayName;
            if (string.IsNullOrWhiteSpace(name) || name.Equals(code, StringComparison.OrdinalIgnoreCase)
                || name.Contains($"({code})", StringComparison.OrdinalIgnoreCase)) return null;
            return name;
        }
        catch (Exception exception) when (exception is ArgumentException or TypeLoadException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
