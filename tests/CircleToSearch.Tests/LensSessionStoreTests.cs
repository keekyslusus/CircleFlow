using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class LensSessionStoreTests
{
    [Fact]
    public void Saved_session_survives_a_round_trip()
    {
        var store = NewStore();
        var session = new LensSession
        {
            IssuedAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
            Cookies = [new LensCookie("NID", "n1", ".google.com", "/"), new LensCookie("AEC", "a1", ".google.com", "/")],
        };

        store.Save(session);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(session.IssuedAt, loaded.IssuedAt);
        Assert.Equal(["NID", "AEC"], loaded.Cookies.Select(c => c.Name));
        Assert.Equal("n1", loaded.Cookies[0].Value);
        Assert.Contains("NID=n1", loaded.CookieHeader());
    }

    [Fact]
    public void Missing_store_loads_as_null()
    {
        Assert.Null(NewStore().Load());
    }

    [Fact]
    public void Corrupted_store_loads_as_null()
    {
        var store = NewStore();
        File.WriteAllText(Path.Combine(storeDirectory(), "lens_session.json"), "{ not json");

        Assert.Null(store.Load());
    }

    [Fact]
    public void Delete_removes_the_session()
    {
        var store = NewStore();
        store.Save(new LensSession { Cookies = [new LensCookie("NID", "n1", ".google.com", "/")] });

        store.Delete();

        Assert.Null(store.Load());
    }

    private static string storeDirectory() => Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", "session-store");

    private static LensSessionStore NewStore()
    {
        Directory.CreateDirectory(storeDirectory());
        foreach (var file in Directory.GetFiles(storeDirectory())) File.Delete(file);
        return new LensSessionStore(storeDirectory());
    }
}
