using System.Text.Json.Serialization;

namespace RobloxModManager.Core;

public enum BootstrapperStyle { Fluent, Classic, Terminal }

public enum AppTheme { System, Light, Dark }

public enum CleanerAge { Never, OneDay, OneWeek, TwoWeeks, OneMonth, TwoMonths }

public class CustomIntegration
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string LaunchArgs { get; set; } = "";
    /// <summary>Close the program when Roblox closes.</summary>
    public bool AutoClose { get; set; } = true;
}

/// <summary>User preferences (Settings.json).</summary>
public class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.Fluent;
    public string BootstrapperTitle { get; set; } = AppInfo.DisplayName;

    public bool ConfirmLaunches { get; set; } = true;
    public bool UseFastFlagManager { get; set; } = true;
    public bool ApplyMods { get; set; } = true;
    public string Channel { get; set; } = Deployment.DefaultChannel;

    public CleanerAge CleanerAge { get; set; } = CleanerAge.TwoWeeks;
    public bool CleanRobloxCache { get; set; } = false;
    public bool CleanRobloxLogs { get; set; } = true;
    public bool CleanAppLogs { get; set; } = true;

    public List<CustomIntegration> CustomIntegrations { get; set; } = new();

    public static AppSettings Load() => JsonFile.Load<AppSettings>(Paths.SettingsFile);

    public void Save() => JsonFile.Save(Paths.SettingsFile, this);
}

/// <summary>Bookkeeping about what is installed (State.json).</summary>
public class AppState
{
    public string VersionGuid { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, string> PackageHashes { get; set; } = new();

    /// <summary>Relative paths (forward slashes) of mod files currently applied to <see cref="VersionGuid"/>.</summary>
    public List<string> ModManifest { get; set; } = new();

    public bool ForceReinstall { get; set; } = false;
    public bool PromptWebView2Install { get; set; } = true;

    /// <summary>Protocol handler commands that were registered before we took over, so uninstall can restore them.</summary>
    public Dictionary<string, string> PreviousProtocolHandlers { get; set; } = new();

    public static AppState Load() => JsonFile.Load<AppState>(Paths.StateFile);

    public void Save() => JsonFile.Save(Paths.StateFile, this);
}

/// <summary>Response of the clientsettings version endpoint.</summary>
public class ClientVersion
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("clientVersionUpload")]
    public string VersionGuid { get; set; } = "";

    [JsonPropertyName("bootstrapperVersion")]
    public string BootstrapperVersion { get; set; } = "";
}
