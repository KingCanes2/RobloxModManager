using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobloxModManager.Core;

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Loads a JSON file, returning defaults when it is missing or unreadable (a copy of a corrupt file is kept).</summary>
    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path))
                return new T();

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
        }
        catch (Exception ex)
        {
            Logger.WriteException("JsonFile::Load", ex);

            try { File.Copy(path, path + ".corrupt", true); } catch { /* ignore */ }

            return new T();
        }
    }

    /// <summary>Writes atomically (temp file + replace) so a crash never leaves a half-written file.</summary>
    public static void Save<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
}
