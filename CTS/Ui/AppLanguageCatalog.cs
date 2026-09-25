using System.Globalization;
using System.IO;

namespace CircleToSearch.Ui;

internal sealed record AppLanguageOption(string Tag, string DisplayName);

internal sealed class AppLanguageCatalog
{
    public AppLanguageCatalog(string languagesDirectory)
    {
        LanguagesDirectory = languagesDirectory;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(languagesDirectory, "*.xaml").ToArray(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { files = []; }
        var available = new List<AppLanguageOption>();
        var problems = new List<string>();
        foreach (var file in files)
        {
            if (Culture(Path.GetFileNameWithoutExtension(file)) is not { } culture) continue;
            // A file that cannot be read would leave the interface in English, so it is not offered.
            if (LocalUiStrings.ReadError(file) is { } error) problems.Add($"{Path.GetFileName(file)} was ignored: {error}");
            else available.Add(new AppLanguageOption(culture.Name, NativeName(culture)));
        }
        Available = available
            .OrderBy(option => option.Tag != "en")
            .ThenBy(option => option.DisplayName, StringComparer.InvariantCultureIgnoreCase)
            .ToArray();
        Problems = problems.AsReadOnly();
    }

    public string LanguagesDirectory { get; }

    public IReadOnlyList<AppLanguageOption> Available { get; }

    public IReadOnlyList<string> Problems { get; }

    // Empty means the Windows display language; so does a saved language whose file was removed.
    public string Find(string tag)
    {
        for (var candidate = Culture(tag); candidate is { Name.Length: > 0 }; candidate = candidate.Parent)
            if (Available.FirstOrDefault(option => option.Tag.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)) is { } option)
                return option.Tag;
        return string.Empty;
    }

    // Resolving to the delivered language lets equal choices, such as "ru" on a ru-RU system, compare equal.
    public CultureInfo Resolve(string tag, CultureInfo system) =>
        (Find(tag) is { Length: > 0 } saved ? saved : Find(system.Name)) is { Length: > 0 } available
            ? CultureInfo.GetCultureInfo(available)
            : system;

    private static CultureInfo? Culture(string name)
    {
        try { return name.Length == 0 ? null : CultureInfo.GetCultureInfo(name, predefinedOnly: true); }
        catch (CultureNotFoundException) { return null; }
    }

    private static string NativeName(CultureInfo culture) =>
        culture.TextInfo.ToUpper(culture.NativeName[0]) + culture.NativeName[1..];
}
