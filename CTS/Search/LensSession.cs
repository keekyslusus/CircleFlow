namespace CircleToSearch.Search;

public sealed record LensCookie(string Name, string Value, string Domain, string Path);

// Anonymous Google cookies (NID/AEC and similar) minted by a real Chromium engine. Google grades
// the session at cookie-issuance time: cookies from a genuine browser unlock full Lens results
// for plain HTTP uploads, self-issued ones do not (verified 2026-08-28).
public sealed record LensSession
{
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;

    public List<LensCookie> Cookies { get; init; } = [];

    public string CookieHeader()
        => string.Join("; ", Cookies.DistinctBy(c => c.Name).Select(c => $"{c.Name}={c.Value}"));
}
