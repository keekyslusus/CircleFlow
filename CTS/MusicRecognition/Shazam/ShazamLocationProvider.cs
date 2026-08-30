using System.Security.Cryptography;

namespace CircleToSearch.MusicRecognition.Shazam;

public sealed record ShazamLocation(
    double Altitude,
    double Latitude,
    double Longitude,
    string Timezone);

public interface IShazamLocationProvider
{
    ShazamLocation Select();
}

public sealed class ShazamLocationProvider : IShazamLocationProvider
{
    private static readonly ShazamLocation[] Locations =
    [
        new(10, 40.7128, -74.0060, "America/New_York"),
        new(2, 25.7617, -80.1918, "America/New_York"),
        new(181, 33.7490, -84.3880, "America/New_York"),
        new(43, 42.3601, -71.0589, "America/New_York"),
        new(12, 39.9526, -75.1652, "America/New_York"),
        new(183, 42.3314, -83.0458, "America/Detroit"),
        new(182, 41.8781, -87.6298, "America/Chicago"),
        new(131, 32.7767, -96.7970, "America/Chicago"),
        new(229, 29.7604, -95.3698, "America/Chicago"),
        new(253, 44.9778, -93.2650, "America/Chicago"),
        new(-2, 29.9511, -90.0715, "America/Chicago"),
        new(1609, 39.7392, -104.9903, "America/Denver"),
        new(1288, 40.7608, -111.8910, "America/Denver"),
        new(832, 43.6150, -116.2023, "America/Boise"),
        new(331, 33.4484, -112.0740, "America/Phoenix"),
        new(71, 34.0522, -118.2437, "America/Los_Angeles"),
        new(16, 37.7749, -122.4194, "America/Los_Angeles"),
        new(53, 47.6062, -122.3321, "America/Los_Angeles"),
        new(610, 36.1699, -115.1398, "America/Los_Angeles"),
        new(15, 45.5152, -122.6784, "America/Los_Angeles"),
        new(19, 32.7157, -117.1611, "America/Los_Angeles"),
        new(31, 61.2181, -149.9003, "America/Anchorage"),
        new(5, 21.3099, -157.8581, "Pacific/Honolulu"),
    ];

    private readonly Func<int, int> _selectIndex;

    public ShazamLocationProvider()
        : this(RandomNumberGenerator.GetInt32)
    {
    }

    internal ShazamLocationProvider(Func<int, int> selectIndex)
    {
        ArgumentNullException.ThrowIfNull(selectIndex);
        _selectIndex = selectIndex;
    }

    internal static IReadOnlyList<ShazamLocation> Profiles => Locations;

    public ShazamLocation Select() => Locations[_selectIndex(Locations.Length)];
}
