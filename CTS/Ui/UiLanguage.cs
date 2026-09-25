using System.Globalization;

namespace CircleToSearch.Ui;

// The current translation; UiStrings reads through it, so text created after a switch uses the new language.
internal sealed class UiLanguage
{
    private readonly LocalUiStrings _english;
    private readonly AppLanguageCatalog _catalog;
    private readonly CultureInfo _system;
    private readonly PluginLog _log;
    private CultureInfo _culture;
    private LocalUiStrings _current;
    private bool _catalogReported;
    private bool _translationReported;

    // Nothing is logged until the first Apply: startup messages need strings before the log folder exists.
    public UiLanguage(LocalUiStrings english, AppLanguageCatalog catalog, CultureInfo system, PluginLog log)
    {
        _english = english;
        _catalog = catalog;
        _system = system;
        _log = log;
        _culture = catalog.Resolve(string.Empty, system);
        _current = english.Translate(catalog.LanguagesDirectory, _culture);
    }

    public AppLanguageCatalog Catalog => _catalog;

    public string Get(string key) => Volatile.Read(ref _current).Get(key);

    public void Apply(string tag)
    {
        var culture = _catalog.Resolve(tag, _system);
        if (!culture.Equals(_culture))
        {
            _culture = culture;
            Volatile.Write(ref _current, _english.Translate(_catalog.LanguagesDirectory, culture));
            _translationReported = false;
        }
        IReadOnlyList<string> problems = _translationReported ? [] : _current.Problems;
        if (!_catalogReported) problems = [.. _catalog.Problems, .. problems];
        foreach (var problem in problems.Distinct()) _log.Warn(nameof(LocalUiStrings), problem);
        _catalogReported = _translationReported = true;
    }
}
