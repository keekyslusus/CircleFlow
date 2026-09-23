namespace CircleToSearch.Search;

internal static class GoogleSearchUrl
{
    internal static bool IsGoogle(Uri? uri) => uri is { Scheme: "https", Host: "google.com" or "www.google.com" };

    internal static bool IsSearch(Uri? uri) => IsGoogle(uri) && uri!.AbsolutePath == "/search";

    internal static IReadOnlyList<(string Name, string Raw)> Parameters(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => (Name: part.Split('=', 2)[0], Raw: part))
            .ToArray();

    internal static bool HasParameter(Uri uri, string name) =>
        Parameters(uri).Any(parameter => parameter.Name == name);

    internal static bool HasParameter(Uri uri, string name, string value) =>
        Parameters(uri).Any(parameter => parameter.Raw == $"{name}={value}");
}
