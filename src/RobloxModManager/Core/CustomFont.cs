using System.Text.Json.Nodes;

namespace RobloxModManager.Core;

/// <summary>
/// Replaces Roblox's UI font. The chosen .ttf is stored as a mod, and every font family definition is
/// pointed at it (Roblox's own font files are left untouched).
/// </summary>
public static class CustomFont
{
    public const string AssetPath = "rbxasset://fonts/CustomFont.ttf";

    public static string FontFile => Path.Combine(Paths.Mods, "content", "fonts", "CustomFont.ttf");
    public static string FamiliesFolder => Path.Combine(Paths.Mods, "content", "fonts", "families");

    public static bool IsApplied => File.Exists(FontFile);

    public static void Apply(string sourceFile)
    {
        if (!sourceFile.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a TrueType font file (.ttf).");

        Directory.CreateDirectory(Path.GetDirectoryName(FontFile)!);
        File.Copy(sourceFile, FontFile, true);
    }

    /// <summary>Removes the font and the generated family files; the next launch restores Roblox's fonts.</summary>
    public static void Remove()
    {
        if (File.Exists(FontFile))
            File.Delete(FontFile);

        if (Directory.Exists(FamiliesFolder))
            Directory.Delete(FamiliesFolder, true);
    }

    /// <summary>Writes a copy of every Roblox font family that uses the custom font. Existing copies are kept,
    /// so a family the user customised by hand is not overwritten.</summary>
    public static int GenerateFamilies(string versionDirectory)
    {
        if (!IsApplied)
            return 0;

        string source = Path.Combine(versionDirectory, "content", "fonts", "families");
        if (!Directory.Exists(source))
            return 0;

        Directory.CreateDirectory(FamiliesFolder);
        int written = 0;

        foreach (string file in Directory.GetFiles(source, "*.json"))
        {
            string target = Path.Combine(FamiliesFolder, Path.GetFileName(file));
            if (File.Exists(target))
                continue;

            try
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject family || family["faces"] is not JsonArray faces)
                    continue;

                foreach (var face in faces)
                {
                    if (face is JsonObject entry)
                        entry["assetId"] = AssetPath;
                }

                File.WriteAllText(target, family.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                written++;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("CustomFont", $"Skipping {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return written;
    }
}
