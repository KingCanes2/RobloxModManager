using System.Security.Cryptography;

namespace RobloxModManager.Core;

public static class Md5Hash
{
    public static string FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return FromStream(stream);
    }

    public static string FromStream(Stream stream) =>
        Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
}
