namespace CircleToSearch.Search;

public static class YandexResultUrlPolicy
{
    public static bool IsAllowed(Uri? url)
        => url is { IsAbsoluteUri: true }
           && url.Scheme == Uri.UriSchemeHttps
           && HostIsYandex(url.Host);

    private static bool HostIsYandex(string host)
        => host is "yandex.ru" or "yandex.kz" or "yandex.com"
           || host.EndsWith(".yandex.ru", StringComparison.Ordinal)
           || host.EndsWith(".yandex.kz", StringComparison.Ordinal);
}
