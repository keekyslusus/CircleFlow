using System.Security.Cryptography;

namespace CircleToSearch.Search;

internal static class ImageUploadName
{
    public static string Create() => $"{RandomNumberGenerator.GetHexString(12, lowercase: true)}.jpg";
}
