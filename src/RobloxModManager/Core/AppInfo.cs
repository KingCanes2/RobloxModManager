namespace RobloxModManager.Core;

/// <summary>
/// Central place for the application's identity. Change <see cref="ProjectName"/> and
/// <see cref="DisplayName"/> to rebrand the app (install folder, exe name and UI text follow).
/// </summary>
public static class AppInfo
{
    public const string ProjectName = "RobloxModManager";
    public const string DisplayName = "Roblox Mod Manager";
    public const string Version = "1.0.0";
    public const string ExecutableName = ProjectName + ".exe";

    public static string UserAgent => $"{ProjectName}/{Version}";
}
