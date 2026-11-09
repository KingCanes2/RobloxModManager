using System.IO.Compression;

namespace RobloxModManager.Core;

public enum PlacementOutcome { Placed, Ambiguous, NotFound, Failed }

/// <summary>What happened to one dropped file.</summary>
public sealed record Placement(string Source, PlacementOutcome Outcome, string? RelativePath, IReadOnlyList<string> Candidates, string? Message);

/// <summary>
/// "Smart drop": works out where a dropped file belongs inside Roblox so people don't have to know the folder layout.
/// A file is matched by name against the installed version (content\sounds\ouch.ogg for "ouch.ogg").
/// A zip mod pack is unpacked with its own folders, after finding the folder in it that lines up with Roblox's.
/// </summary>
public static class ModPlacer
{
    private static readonly string[] RobloxTopFolders =
    {
        "content", "PlatformContent", "ExtraContent", "ClientSettings", "shaders", "ssl"
    };

    /// <summary>Every file in the version folder, indexed by file name.</summary>
    public static Dictionary<string, List<string>> IndexVersion(string versionDirectory)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(versionDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(versionDirectory, path).Replace('\\', '/');

            // Don't suggest replacing the game's own program files.
            if (!relative.Contains('/') && (relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                            || relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                continue;

            string name = Path.GetFileName(path);
            if (!index.TryGetValue(name, out var list))
                index[name] = list = new List<string>();
            list.Add(relative);
        }

        return index;
    }

    /// <summary>Most likely spot first: inside content\, then the shortest path.</summary>
    private static List<string> Rank(IEnumerable<string> candidates) =>
        candidates.OrderBy(c => c.StartsWith("content/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                  .ThenBy(c => c.Count(ch => ch == '/'))
                  .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                  .ToList();

    public static Placement PlaceFile(string source, Dictionary<string, List<string>> index, string modsDirectory, string? fileName = null)
    {
        string name = fileName ?? Path.GetFileName(source);

        if (!index.TryGetValue(name, out var matches) || matches.Count == 0)
            return new Placement(source, PlacementOutcome.NotFound, null, Array.Empty<string>(),
                $"Roblox has no file called {name}, so there's nothing for it to replace.");

        var ranked = Rank(matches);

        if (ranked.Count > 1)
            return new Placement(source, PlacementOutcome.Ambiguous, null, ranked,
                $"Roblox has {ranked.Count} files called {name}.");

        return CopyTo(source, ranked[0], modsDirectory);
    }

    public static Placement CopyTo(string source, string relativePath, string modsDirectory)
    {
        try
        {
            string target = Path.Combine(modsDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
            return new Placement(source, PlacementOutcome.Placed, relativePath, new[] { relativePath }, null);
        }
        catch (Exception ex)
        {
            return new Placement(source, PlacementOutcome.Failed, relativePath, Array.Empty<string>(), ex.Message);
        }
    }

    /// <summary>
    /// Unpacks a mod pack. Packs are often zipped with an extra wrapper folder ("MyPack/content/sounds/...")
    /// so the deepest folder whose children look like Roblox's top-level folders becomes the root.
    /// Loose files with no recognisable layout are placed by name instead.
    /// </summary>
    public static List<Placement> PlaceZip(string zipPath, Dictionary<string, List<string>> index, string modsDirectory)
    {
        var results = new List<Placement>();
        string modsRoot = Path.GetFullPath(modsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        var files = archive.Entries
            .Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\'))
            .Select(e => (Entry: e, Path: e.FullName.Replace('\\', '/').TrimStart('/')))
            .Where(f => !f.Path.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
                        && !Path.GetFileName(f.Path).StartsWith("._", StringComparison.Ordinal))
            .ToList();

        string? root = FindRoot(files.Select(f => f.Path));

        foreach (var (entry, path) in files)
        {
            string label = $"{Path.GetFileName(zipPath)} > {path}";

            // Inside the recognised layout: keep the pack's own folders.
            if (root is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                string relative = path[root.Length..];
                string top = relative.Split('/')[0];

                if (relative.Contains('/') && RobloxTopFolders.Contains(top, StringComparer.OrdinalIgnoreCase))
                {
                    string target = Path.GetFullPath(Path.Combine(modsDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));

                    if (!target.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new Placement(label, PlacementOutcome.Failed, null, Array.Empty<string>(), "Unsafe path inside the zip; skipped."));
                        continue;
                    }

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, true);
                        results.Add(new Placement(label, PlacementOutcome.Placed, relative, new[] { relative }, null));
                    }
                    catch (Exception ex)
                    {
                        results.Add(new Placement(label, PlacementOutcome.Failed, relative, Array.Empty<string>(), ex.Message));
                    }

                    continue;
                }
            }

            // Readme files and the like don't belong in Roblox.
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".txt" or ".md" or ".url" or ".html" or ".pdf" or "")
                continue;

            // Otherwise match by file name, like a single dropped file.
            string temp = Path.Combine(Path.GetTempPath(), $"rmm-{Guid.NewGuid():N}-{Path.GetFileName(path)}");
            try
            {
                entry.ExtractToFile(temp, true);
                var placement = PlaceFile(temp, index, modsDirectory, Path.GetFileName(path));

                if (placement.Outcome == PlacementOutcome.Ambiguous)
                {
                    // Can't ask about every file in a pack: take the most likely spot and say so.
                    placement = CopyTo(temp, placement.Candidates[0], modsDirectory) with
                    {
                        Message = $"Several files share this name; used {placement.Candidates[0]}."
                    };
                }

                results.Add(placement with { Source = label });
            }
            finally
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }

        return results;
    }

    /// <summary>The path prefix ("" or "Wrapper/") under which Roblox's top-level folders sit, or null.</summary>
    public static string? FindRoot(IEnumerable<string> paths)
    {
        string? best = null;

        foreach (string path in paths)
        {
            string[] parts = path.Split('/');

            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!RobloxTopFolders.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
                    continue;

                string prefix = i == 0 ? "" : string.Join('/', parts.Take(i)) + "/";
                if (best is null || prefix.Length < best.Length)
                    best = prefix;
                break;
            }
        }

        return best;
    }
}
