using System.Globalization;
using CircleToSearch.Ui;

namespace CircleToSearch.Translation;

internal static class TranslationTargetLanguage
{
    public static string From(CultureInfo culture) => string.IsNullOrWhiteSpace(culture.Name) ? "en" : culture.Name;

    // An empty or unknown choice follows the Windows display language.
    public static string Resolve(string? selected, CultureInfo culture) => TranslationLanguages.Find(selected) ?? From(culture);

    // Google translates to a language without a region, so name the language that is actually requested.
    public static string DisplayName(string target, UiStrings strings) =>
        TranslationLanguages.DisplayName(ScreenTranslationWorkflow.NormalizeTarget(target), strings);
}
