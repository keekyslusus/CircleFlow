using System.Globalization;
using Windows.Globalization;

namespace CircleToSearch.Translation;

internal static class TranslationTargetLanguage
{
    public static string From(CultureInfo culture) => string.IsNullOrWhiteSpace(culture.Name) ? "en" : culture.Name;

    // Google translates to a language without a region, so name the language that is actually requested.
    public static string DisplayName(CultureInfo culture)
    {
        var tag = ScreenTranslationWorkflow.NormalizeTarget(From(culture));
        try { return new Language(tag).DisplayName; }
        catch (Exception exception) when (exception is ArgumentException or TypeLoadException or PlatformNotSupportedException) { }
        try { return CultureInfo.GetCultureInfo(tag).DisplayName; }
        catch (CultureNotFoundException) { return tag; }
    }
}
