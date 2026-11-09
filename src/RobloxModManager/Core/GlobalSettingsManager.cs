using System.Xml.Linq;

namespace RobloxModManager.Core;

/// <summary>Edits Roblox's GlobalBasicSettings_13.xml (graphics quality, FPS cap, sensitivity...).</summary>
public sealed class GlobalSettingsManager
{
    private static readonly IReadOnlyDictionary<string, (string Type, string Name)> Presets = new Dictionary<string, (string, string)>
    {
        ["Rendering.FramerateCap"] = ("int", "FramerateCap"),
        ["Rendering.SavedQualityLevel"] = ("token", "SavedQualityLevel"), // 0 is automatic
        ["User.MouseSensitivity"] = ("float", "MouseSensitivity"),
        ["User.VREnabled"] = ("bool", "VREnabled"),
        ["UI.Transparency"] = ("float", "PreferredTransparency"),
        ["UI.ReducedMotion"] = ("bool", "ReducedMotion"),
        ["UI.FontSize"] = ("token", "PreferredTextSize"),
    };

    public XDocument? Document { get; private set; }

    public string FilePath => Paths.GlobalBasicSettings;

    public bool Loaded => Document is not null;

    public bool Load()
    {
        try
        {
            Document = File.Exists(FilePath) ? XDocument.Load(FilePath) : null;
        }
        catch (Exception ex)
        {
            Logger.WriteException("GlobalSettings::Load", ex);
            Document = null;
        }

        return Loaded;
    }

    /// <summary>Creates a minimal settings file for people who have never launched Roblox.</summary>
    public bool CreateTemplate()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string referent = "RBX" + Guid.NewGuid().ToString("N").ToUpperInvariant();

            File.WriteAllText(FilePath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<roblox xmlns:xmime=\"http://www.w3.org/2005/05/xmlmime\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"http://www.roblox.com/roblox.xsd\" version=\"4\">\n" +
                "\t<External>null</External>\n" +
                "\t<External>nil</External>\n" +
                $"\t<Item class=\"UserGameSettings\" referent=\"{referent}\">\n" +
                "\t\t<Properties>\n" +
                "\t\t\t<int name=\"FramerateCap\">60</int>\n" +
                "\t\t\t<token name=\"SavedQualityLevel\">0</token>\n" +
                "\t\t\t<float name=\"MouseSensitivity\">1</float>\n" +
                "\t\t\t<bool name=\"VREnabled\">false</bool>\n" +
                "\t\t\t<float name=\"PreferredTransparency\">1</float>\n" +
                "\t\t\t<bool name=\"ReducedMotion\">false</bool>\n" +
                "\t\t\t<token name=\"PreferredTextSize\">1</token>\n" +
                "\t\t</Properties>\n" +
                "\t</Item>\n" +
                "</roblox>\n");

            return Load();
        }
        catch (Exception ex)
        {
            Logger.WriteException("GlobalSettings::CreateTemplate", ex);
            return false;
        }
    }

    private XElement? Properties() =>
        Document?.Descendants("Item")
            .FirstOrDefault(item => (string?)item.Attribute("class") == "UserGameSettings")
            ?.Element("Properties");

    private XElement? Find(string preset)
    {
        var (type, name) = Presets[preset];
        return Properties()?.Elements().FirstOrDefault(e => e.Name.LocalName == type && (string?)e.Attribute("name") == name);
    }

    public string? Get(string preset) => Find(preset)?.Value;

    /// <summary>Sets a value, creating the property (with Roblox's own element type) if the file lacks it.</summary>
    public void Set(string preset, string value)
    {
        var properties = Properties();
        if (properties is null)
            return;

        var element = Find(preset);
        if (element is null)
        {
            var (type, name) = Presets[preset];
            element = new XElement(type, new XAttribute("name", name));
            properties.Add(element);
        }

        element.Value = value;
    }

    public bool IsReadOnly => File.Exists(FilePath) && (File.GetAttributes(FilePath) & FileAttributes.ReadOnly) != 0;

    public void SetReadOnly(bool readOnly)
    {
        if (!File.Exists(FilePath))
            return;

        var attributes = File.GetAttributes(FilePath);
        attributes = readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly;
        File.SetAttributes(FilePath, attributes);
    }

    public void Save(bool readOnly)
    {
        if (Document is null)
            return;

        SetReadOnly(false);
        Document.Save(FilePath);
        SetReadOnly(readOnly);
    }
}
