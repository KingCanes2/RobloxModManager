namespace RobloxModManager.Core;

public sealed class ModApplyResult
{
    /// <summary>Relative paths (forward slashes) that are applied now. Becomes the new manifest.</summary>
    public List<string> Applied { get; } = new();

    public int Copied { get; set; }
    public int Restored { get; set; }
    public List<string> Failures { get; } = new();

    public bool Success => Failures.Count == 0;
}

/// <summary>
/// Copies everything in the mods folder over a Roblox version folder. The original of every replaced
/// file is backed up first, so removing a mod puts the original back.
/// </summary>
public static class ModManager
{
    public static ModApplyResult Apply(
        string versionDirectory,
        string modsDirectory,
        string backupDirectory,
        IReadOnlyCollection<string> previousManifest,
        Func<string, bool>? shouldSkip,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        const string ident = "ModManager::Apply";
        var result = new ModApplyResult();

        Directory.CreateDirectory(modsDirectory);

        var previous = new HashSet<string>(previousManifest, StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // relative path -> file to copy. Overrides (for example a game profile's flags) replace or add entries.
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(modsDirectory, "*", SearchOption.AllDirectories))
            sources[Path.GetRelativePath(modsDirectory, path).Replace('\\', '/')] = path;

        if (overrides is not null)
            foreach (var (relativePath, source) in overrides)
                sources[relativePath.Replace('\\', '/')] = source;

        foreach (var (relative, file) in sources)
        {
            ct.ThrowIfCancellationRequested();

            if (IsIgnored(relative) || (shouldSkip?.Invoke(relative) ?? false))
                continue;

            wanted.Add(relative);

            string destination = ToPath(versionDirectory, relative);
            string backup = ToPath(backupDirectory, relative);
            bool alreadyTracked = previous.Contains(relative);

            try
            {
                if (File.Exists(destination) && FilesEqual(file, destination))
                {
                    result.Applied.Add(relative);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                // Keep the first original only; later edits of the same mod must not overwrite it.
                if (!alreadyTracked && File.Exists(destination) && !File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, true);
                }

                ClearReadOnly(destination);
                File.Copy(file, destination, true);

                result.Copied++;
                result.Applied.Add(relative);
                Logger.WriteLine(ident, $"{relative} applied");
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ident, $"Failed to apply {relative}: {ex.Message}");
                result.Failures.Add(relative);

                if (alreadyTracked)
                    result.Applied.Add(relative);
            }
        }

        // Mods that were applied before but are gone (or now skipped): put the originals back.
        foreach (string relative in previous)
        {
            ct.ThrowIfCancellationRequested();

            if (wanted.Contains(relative))
                continue;

            string destination = ToPath(versionDirectory, relative);
            string backup = ToPath(backupDirectory, relative);

            try
            {
                if (File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    ClearReadOnly(destination);
                    File.Copy(backup, destination, true);
                    File.Delete(backup);
                    result.Restored++;
                    Logger.WriteLine(ident, $"{relative} restored from backup");
                }
                else if (File.Exists(destination))
                {
                    // The mod added a brand-new file, so there is no original to restore.
                    ClearReadOnly(destination);
                    File.Delete(destination);
                    result.Restored++;
                    Logger.WriteLine(ident, $"{relative} removed");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ident, $"Failed to restore {relative}: {ex.Message}");
                result.Failures.Add(relative);
                result.Applied.Add(relative); // still modified, keep tracking it
            }
        }

        return result;
    }

    /// <summary>Lists the files the mods folder would apply (for the Mods page).</summary>
    public static List<(string Relative, long Size)> Enumerate(string modsDirectory, Func<string, bool>? shouldSkip = null)
    {
        var list = new List<(string, long)>();

        if (!Directory.Exists(modsDirectory))
            return list;

        foreach (string file in Directory.EnumerateFiles(modsDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(modsDirectory, file).Replace('\\', '/');

            if (IsIgnored(relative) || (shouldSkip?.Invoke(relative) ?? false))
                continue;

            list.Add((relative, new FileInfo(file).Length));
        }

        list.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static bool IsIgnored(string relative)
    {
        if (relative.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            return true;

        // Notes file we create in the mods folder; never copied into Roblox.
        if (relative.Equals("README.txt", StringComparison.OrdinalIgnoreCase))
            return true;

        // Replacing avatar head/body meshes changes how other players' characters look and hit,
        // which Roblox treats as an unfair advantage, so those files are never applied.
        if (relative.EndsWith(".mesh", StringComparison.OrdinalIgnoreCase)
            && (relative.StartsWith("content/avatar/heads", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("content/avatar/compositing", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("content/avatar/meshes", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static string ToPath(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path))
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static bool FilesEqual(string a, string b)
    {
        var infoA = new FileInfo(a);
        var infoB = new FileInfo(b);

        if (infoA.Length != infoB.Length)
            return false;

        using var streamA = infoA.OpenRead();
        using var streamB = infoB.OpenRead();

        var bufferA = new byte[81920];
        var bufferB = new byte[81920];

        while (true)
        {
            int readA = streamA.ReadAtLeast(bufferA, bufferA.Length, throwOnEndOfStream: false);
            int readB = streamB.ReadAtLeast(bufferB, bufferB.Length, throwOnEndOfStream: false);

            if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                return false;

            if (readA == 0)
                return true;
        }
    }
}
