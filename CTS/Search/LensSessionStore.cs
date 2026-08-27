using System.IO;
using System.Text.Json;

namespace CircleToSearch.Search;

public sealed class LensSessionStore
{
    private readonly string _path;

    public LensSessionStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "lens_session.json");
    }

    public LensSession? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<LensSession>(File.ReadAllText(_path));
        }
        catch
        {
            return null;
        }
    }

    public void Save(LensSession session)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(session));
        }
        catch
        {
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(_path);
        }
        catch
        {
        }
    }
}
