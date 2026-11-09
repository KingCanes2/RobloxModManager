using System.Text.Json;

namespace RobloxModManager.Core;

/// <summary>
/// Edits ClientAppSettings.json. All values are kept as strings ("True", "60"), which is what Roblox accepts.
/// The file lives in the mods folder, so <see cref="ModManager"/> copies it into each Roblox version.
/// </summary>
public class FastFlagManager
{
    public static readonly IReadOnlyDictionary<string, string> PresetFlags = new Dictionary<string, string>
    {
        ["Rendering.ManualFullscreen"] = "FFlagHandleAltEnterFullscreenManually",
        ["Rendering.DisableScaling"] = "DFFlagDisableDPIScale",
        ["Rendering.MSAA"] = "FIntDebugForceMSAASamples",
        ["Rendering.FRMQualityOverride"] = "DFIntDebugFRMQualityLevelOverride",

        ["Rendering.Mode.D3D11"] = "FFlagDebugGraphicsPreferD3D11",
        ["Rendering.Mode.Vulkan"] = "FFlagDebugGraphicsPreferVulkan",

        // "Static" is a marker we store to remember the chosen mesh detail; Roblox ignores unknown flags.
        ["Geometry.MeshLOD.Static"] = "DFIntCSGLevelOfDetailSwitchingDistanceStatic",
        ["Geometry.MeshLOD.L0"] = "DFIntCSGLevelOfDetailSwitchingDistance",
        ["Geometry.MeshLOD.L12"] = "DFIntCSGLevelOfDetailSwitchingDistanceL12",
        ["Geometry.MeshLOD.L23"] = "DFIntCSGLevelOfDetailSwitchingDistanceL23",
        ["Geometry.MeshLOD.L34"] = "DFIntCSGLevelOfDetailSwitchingDistanceL34",
    };

    public static readonly string[] MeshLodLevels = { "L0", "L12", "L23", "L34" };

    public Dictionary<string, string> Flags { get; private set; } = new(StringComparer.Ordinal);

    public string FilePath => Paths.FlagsFile;

    public void Load()
    {
        Flags = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(FilePath))
                return;

            if (TryParse(File.ReadAllText(FilePath), out var parsed, out string? error))
                Flags = parsed;
            else
                Logger.WriteLine("FastFlagManager::Load", $"Could not read flags: {error}");
        }
        catch (Exception ex)
        {
            Logger.WriteException("FastFlagManager::Load", ex);
        }
    }

    /// <summary>Accepts both string values ("True") and native JSON values (true, 60).</summary>
    public static bool TryParse(string json, out Dictionary<string, string> flags, out string? error)
    {
        flags = new Dictionary<string, string>(StringComparer.Ordinal);
        error = null;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Expected a JSON object that maps flag names to values.";
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                string? value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => "True",
                    JsonValueKind.False => "False",
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null
                };

                if (value is not null && !string.IsNullOrWhiteSpace(property.Name))
                    flags[property.Name.Trim()] = value;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(new SortedDictionary<string, string>(Flags, StringComparer.Ordinal), JsonFile.Options);

    public void Save()
    {
        if (Flags.Count == 0)
        {
            // No flags means no file; the mod manager then removes it from Roblox as well.
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            return;
        }

        JsonFile.Save(FilePath, new SortedDictionary<string, string>(Flags, StringComparer.Ordinal));
    }

    public string? Get(string name) => Flags.TryGetValue(name, out string? value) ? value : null;

    /// <summary>Sets a flag, or removes it when <paramref name="value"/> is null.</summary>
    public void Set(string name, string? value)
    {
        if (value is null)
            Flags.Remove(name);
        else
            Flags[name] = value;
    }

    public string? GetPreset(string preset) => Get(PresetFlags[preset]);

    public void SetPreset(string preset, string? value) => Set(PresetFlags[preset], value);

    public void Clear() => Flags.Clear();
}
