namespace RobloxModManager.Core;

/// <summary>All folders and files the app uses. Everything hangs off <see cref="Base"/>.</summary>
public static class Paths
{
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string StartMenuPrograms => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
    public static string ProcessPath => Environment.ProcessPath ?? string.Empty;

    public static string DefaultBase => Path.Combine(LocalAppData, AppInfo.ProjectName);

    public static string Base { get; private set; } = DefaultBase;

    public static void Initialize(string baseDirectory) => Base = Path.GetFullPath(baseDirectory);

    public static string Application => Path.Combine(Base, AppInfo.ExecutableName);
    public static string Downloads => Path.Combine(Base, "Downloads");
    public static string Versions => Path.Combine(Base, "Versions");
    public static string Mods => Path.Combine(Base, "Mods");
    public static string ModBackups => Path.Combine(Base, "ModBackups");
    public static string Logs => Path.Combine(Base, "Logs");
    public static string SettingsFile => Path.Combine(Base, "Settings.json");
    public static string StateFile => Path.Combine(Base, "State.json");
    public static string BootstrapperLock => Path.Combine(Base, "bootstrapper.lock");

    /// <summary>Fast flags live inside the mods folder so they are applied like any other mod file.</summary>
    public static string FlagsFile => Path.Combine(Mods, "ClientSettings", "ClientAppSettings.json");

    /// <summary>Roblox's own data folder (logs, cache, GlobalBasicSettings). Settable for tests.</summary>
    public static string Roblox { get; set; } = Path.Combine(LocalAppData, "Roblox");
    public static string RobloxLogs => Path.Combine(Roblox, "logs");
    public static string RobloxCache => Path.Combine(Roblox, "rbx-storage");
    public static string GlobalBasicSettings => Path.Combine(Roblox, "GlobalBasicSettings_13.xml");

    public static string VersionDirectory(string versionGuid) => Path.Combine(Versions, versionGuid);

    public static bool IsSamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
