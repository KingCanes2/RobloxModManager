using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RobloxModManager.Core;

/// <summary>Registry entries (roblox:/roblox-player: links, Apps &amp; features) and shortcut files.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsIntegration
{
    public static readonly string[] PlayerProtocols = { "roblox", "roblox-player" };

    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.ProjectName;

    // ------------------------------------------------------------------ protocol handlers

    public static string? GetProtocolCommand(string protocol)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{protocol}\shell\open\command");
        return key?.GetValue("") as string;
    }

    private static bool IsOurCommand(string command) =>
        command.Contains(AppInfo.ExecutableName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Takes over roblox: and roblox-player: links, remembering whatever was registered before.</summary>
    public static void RegisterPlayerProtocols(string exePath, AppState state)
    {
        string command = $"\"{exePath}\" -player \"%1\"";

        foreach (string protocol in PlayerProtocols)
        {
            string? existing = GetProtocolCommand(protocol);

            if (existing is not null && !IsOurCommand(existing) && !state.PreviousProtocolHandlers.ContainsKey(protocol))
                state.PreviousProtocolHandlers[protocol] = existing;

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}");
            key.SetValue("", "URL: Roblox Protocol");
            key.SetValue("URL Protocol", "");

            using (var icon = key.CreateSubKey("DefaultIcon"))
                icon.SetValue("", exePath);

            using (var open = key.CreateSubKey(@"shell\open\command"))
                open.SetValue("", command);
        }
    }

    /// <summary>Hands links back to the previous handler (usually Roblox itself) or removes ours.</summary>
    public static void UnregisterPlayerProtocols(AppState state)
    {
        foreach (string protocol in PlayerProtocols)
        {
            string? current = GetProtocolCommand(protocol);

            // Another program took over after us; leave its registration alone.
            if (current is not null && !IsOurCommand(current))
                continue;

            if (state.PreviousProtocolHandlers.TryGetValue(protocol, out string? previous)
                && TryGetExecutable(previous) is { } previousExe
                && File.Exists(previousExe))
            {
                using var open = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}\shell\open\command");
                open.SetValue("", previous);

                using var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}\DefaultIcon");
                icon.SetValue("", previousExe);
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{protocol}", false);
            }
        }
    }

    private static string? TryGetExecutable(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command.Substring(1, end - 1) : null;
        }

        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    // ------------------------------------------------------------------ Apps & features entry

    public static void RegisterUninstall(string installDirectory, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", AppInfo.DisplayName);
        key.SetValue("DisplayVersion", AppInfo.Version);
        key.SetValue("Publisher", AppInfo.DisplayName);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("UninstallString", $"\"{exePath}\" -uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static string? GetInstallLocation()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        return key?.GetValue("InstallLocation") as string;
    }

    public static void RemoveUninstall() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);

    /// <summary>True when Windows apps are set to the light theme (used by the "System" app theme).</summary>
    public static bool AppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    // ------------------------------------------------------------------ shortcuts

    public static void CreateShortcut(string shortcutPath, string target, string arguments, string description)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is not available, so shortcuts cannot be created.");

        dynamic shell = Activator.CreateInstance(shellType)!;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = target;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = target + ",0";
            shortcut.Description = description;
            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}

public enum ShortcutKind { DesktopMenu, StartMenuMenu, DesktopPlayer, DesktopSettings }

[SupportedOSPlatform("windows")]
public static class Shortcuts
{
    public static string PathFor(ShortcutKind kind) => kind switch
    {
        ShortcutKind.DesktopMenu => Path.Combine(Paths.Desktop, $"{AppInfo.DisplayName}.lnk"),
        ShortcutKind.StartMenuMenu => Path.Combine(Paths.StartMenuPrograms, $"{AppInfo.DisplayName}.lnk"),
        ShortcutKind.DesktopPlayer => Path.Combine(Paths.Desktop, "Launch Roblox.lnk"),
        ShortcutKind.DesktopSettings => Path.Combine(Paths.Desktop, $"{AppInfo.DisplayName} Settings.lnk"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static (string Arguments, string Description) Describe(ShortcutKind kind) => kind switch
    {
        ShortcutKind.DesktopPlayer => ("-player", "Launch Roblox"),
        ShortcutKind.DesktopSettings => ("-settings", $"{AppInfo.DisplayName} settings"),
        _ => ("", AppInfo.DisplayName)
    };

    public static bool Exists(ShortcutKind kind) => File.Exists(PathFor(kind));

    public static void Create(ShortcutKind kind)
    {
        var (arguments, description) = Describe(kind);
        WindowsIntegration.CreateShortcut(PathFor(kind), Paths.Application, arguments, description);
    }

    public static void Remove(ShortcutKind kind)
    {
        try
        {
            string path = PathFor(kind);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Shortcuts", $"Could not remove {kind}: {ex.Message}");
        }
    }

    public static void Set(ShortcutKind kind, bool enabled)
    {
        if (enabled && !Exists(kind))
            Create(kind);
        else if (!enabled && Exists(kind))
            Remove(kind);
    }
}
