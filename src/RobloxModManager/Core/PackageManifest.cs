namespace RobloxModManager.Core;

public class Package
{
    public string Name { get; set; } = "";
    public string Signature { get; set; } = "";
    public long PackedSize { get; set; }
    public long Size { get; set; }

    /// <summary>Downloaded zips are cached by their MD5 signature.</summary>
    public string DownloadPath => Path.Combine(Paths.Downloads, Signature);

    public override string ToString() => $"[{Signature}] {Name}";
}

/// <summary>Parser for Roblox's "v0" rbxPkgManifest.txt (name, md5, packed size, size - four lines per package).</summary>
public class PackageManifest : List<Package>
{
    public PackageManifest(string data)
    {
        using var reader = new StringReader(data);
        string? version = reader.ReadLine()?.Trim();

        if (version != "v0")
            throw new NotSupportedException($"Unexpected package manifest version: {version} (expected v0).");

        while (true)
        {
            string? name = reader.ReadLine()?.Trim();
            string? signature = reader.ReadLine()?.Trim();
            string? rawPackedSize = reader.ReadLine()?.Trim();
            string? rawSize = reader.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(signature)
                || string.IsNullOrEmpty(rawPackedSize) || string.IsNullOrEmpty(rawSize))
                break;

            // The launcher exe is listed last and is not needed.
            if (name == "RobloxPlayerLauncher.exe")
                break;

            Add(new Package
            {
                Name = name,
                Signature = signature.ToLowerInvariant(),
                PackedSize = long.Parse(rawPackedSize),
                Size = long.Parse(rawSize)
            });
        }
    }
}

/// <summary>Which folder inside the version directory each package is extracted to.</summary>
public static class PackageMaps
{
    public static readonly IReadOnlyDictionary<string, string> Player = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxApp.zip"] = "",
        ["Libraries.zip"] = "",
        ["redist.zip"] = "",
        ["shaders.zip"] = @"shaders\",
        ["ssl.zip"] = @"ssl\",

        // WebView2 is only extracted when the runtime has to be installed.
        ["WebView2.zip"] = "",
        ["WebView2RuntimeInstaller.zip"] = @"WebView2RuntimeInstaller\",

        ["content-avatar.zip"] = @"content\avatar\",
        ["content-configs.zip"] = @"content\configs\",
        ["content-fonts.zip"] = @"content\fonts\",
        ["content-sky.zip"] = @"content\sky\",
        ["content-sounds.zip"] = @"content\sounds\",
        ["content-textures2.zip"] = @"content\textures\",
        ["content-models.zip"] = @"content\models\",

        ["content-textures3.zip"] = @"PlatformContent\pc\textures\",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain\",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts\",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries\",

        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages\",
        ["extracontent-translations.zip"] = @"ExtraContent\translations\",
        ["extracontent-models.zip"] = @"ExtraContent\models\",
        ["extracontent-textures.zip"] = @"ExtraContent\textures\",
        ["extracontent-places.zip"] = @"ExtraContent\places\",
    };

    /// <summary>
    /// Returns the target sub-folder for a package. Unknown packages follow Roblox's naming convention
    /// (content-x.zip -> content\x\) and otherwise fall back to the version root.
    /// </summary>
    public static string Resolve(string packageName, out bool known)
    {
        if (Player.TryGetValue(packageName, out string? mapped))
        {
            known = true;
            return mapped;
        }

        known = false;
        string stem = Path.GetFileNameWithoutExtension(packageName);

        if (stem.StartsWith("content-", StringComparison.OrdinalIgnoreCase))
            return @"content\" + stem["content-".Length..] + @"\";

        if (stem.StartsWith("extracontent-", StringComparison.OrdinalIgnoreCase))
            return @"ExtraContent\" + stem["extracontent-".Length..] + @"\";

        return "";
    }

    /// <summary>Combines a root with a map entry that uses either slash style.</summary>
    public static string Combine(string root, string relative)
    {
        string path = root;
        foreach (string part in relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            path = Path.Combine(path, part);
        return path;
    }
}
