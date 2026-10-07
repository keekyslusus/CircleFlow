using System.IO;
using System.Text.Json;

namespace CircleToSearch.Search.Browser;

internal sealed record CosmeticFilter(IReadOnlyList<string> Hosts, IReadOnlyList<string> ExcludedHosts, string Selector);

// Reads uBlock Origin cosmetic filters ("host##selector") so rules made with uBO's picker can be pasted in as is.
internal static class CosmeticFilters
{
    // Read on every call so an edited file applies to the next browser window without a restart.
    internal static string LoadScript(string path, PluginLog log)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.SafeError(nameof(CosmeticFilters), "read-filters", exception);
            lines = [];
        }
        return CreateScript(Parse(lines));
    }

    internal static IReadOnlyList<CosmeticFilter> Parse(IEnumerable<string> lines)
    {
        var filters = new List<CosmeticFilter>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '!' or '[') continue;
            var separator = line.IndexOf("##", StringComparison.Ordinal);
            if (separator < 0) continue;
            var selector = line[(separator + 2)..].Trim();
            // Scriptlets (+js) and HTML filters (^) need uBO itself; only plain CSS selectors can be applied here.
            if (selector.Length == 0 || selector[0] is '+' or '^') continue;

            var hosts = new List<string>();
            var excluded = new List<string>();
            foreach (var entry in line[..separator].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry[0] == '~') excluded.Add(entry[1..].ToLowerInvariant());
                else hosts.Add(entry.ToLowerInvariant());
            }
            filters.Add(new CosmeticFilter(hosts, excluded, selector));
        }
        return filters;
    }

    internal static string CreateScript(IReadOnlyList<CosmeticFilter> filters)
    {
        var rules = JsonSerializer.Serialize(filters.Select(filter => new
        {
            hosts = filter.Hosts,
            excluded = filter.ExcludedHosts,
            selector = filter.Selector,
        }));
        return $$"""
            (() => {
                const installationKey = '__circleFlowCosmeticFilters_v1__';
                if (window[installationKey]) return;
                window[installationKey] = true;

                const rules = {{rules}};
                const host = location.hostname.toLowerCase();
                // "google.*" follows uBO and matches google on any top-level domain, such as google.co.uk.
                const matches = pattern => pattern.endsWith('.*')
                    ? host.split('.').some((_, index, labels) =>
                        labels.slice(index).join('.').startsWith(pattern.slice(0, -1)))
                    : host === pattern || host.endsWith('.' + pattern);
                const sheet = new CSSStyleSheet();
                for (const rule of rules) {
                    if (rule.hosts.length > 0 && !rule.hosts.some(matches)) continue;
                    if (rule.excluded.some(matches)) continue;
                    // One rule per selector, so a selector the browser rejects cannot disable the others.
                    try { sheet.insertRule(`${rule.selector}{display:none!important}`, sheet.cssRules.length); }
                    catch { }
                }
                if (sheet.cssRules.length === 0) return;
                document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
                // A page that replaces the whole list would otherwise drop these rules without notice.
                const adopted = Object.getOwnPropertyDescriptor(Document.prototype, 'adoptedStyleSheets');
                if (adopted?.set)
                    Object.defineProperty(document, 'adoptedStyleSheets', {
                        configurable: true,
                        get() { return adopted.get.call(this); },
                        set(sheets) { adopted.set.call(this, [...sheets].includes(sheet) ? sheets : [...sheets, sheet]); },
                    });
            })();
            """;
    }
}
