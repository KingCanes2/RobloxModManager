using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

namespace RobloxModManager.Core;

[SupportedOSPlatform("windows")]
public static class InstallService
{
    /// <summary>True when the running exe is the installed copy (as recorded in Apps &amp; features).</summary>
    public static bool IsRunningFromInstall(out string? installDirectory)
    {
        installDirectory = WindowsIntegration.GetInstallLocation();

        return installDirectory is not null
            && Paths.IsSamePath(Paths.ProcessPath, Path.Combine(installDirectory, AppInfo.ExecutableName));
    }

    public static bool IsInstalled(out string? installDirectory)
    {
        installDirectory = WindowsIntegration.GetInstallLocation();
        return installDirectory is not null && File.Exists(Path.Combine(installDirectory, AppInfo.ExecutableName));
    }

    public static void Install(string directory, bool desktopShortcut, bool startMenuShortcut)
    {
        Paths.Initialize(directory);
        Directory.CreateDirectory(Paths.Base);
        Directory.CreateDirectory(Paths.Mods);
        Directory.CreateDirectory(Paths.Logs);

        CopyApplicationFiles(Path.GetDirectoryName(Paths.ProcessPath)!, Paths.Base);

        if (!File.Exists(Paths.SettingsFile))
            new AppSettings().Save();

        var state = AppState.Load();
        WindowsIntegration.RegisterPlayerProtocols(Paths.Application, state);
        state.Save();

        WindowsIntegration.RegisterUninstall(Paths.Base, Paths.Application);

        Shortcuts.Set(ShortcutKind.DesktopMenu, desktopShortcut);
        Shortcuts.Set(ShortcutKind.StartMenuMenu, startMenuShortcut);
    }

    /// <summary>
    /// A single-file publish is one exe. A normal build is an exe plus dlls/json, so copy those too
    /// (never the settings or state files).
    /// </summary>
    private static void CopyApplicationFiles(string sourceDirectory, string targetDirectory)
    {
        bool singleFile = string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);

        if (singleFile)
        {
            if (!Paths.IsSamePath(Paths.ProcessPath, Paths.Application))
                File.Copy(Paths.ProcessPath, Paths.Application, true);

            return;
        }

        if (Paths.IsSamePath(sourceDirectory, targetDirectory))
            return;

        foreach (string file in Directory.GetFiles(sourceDirectory))
        {
            string name = Path.GetFileName(file);
            string extension = Path.GetExtension(file).ToLowerInvariant();

            bool wanted = extension is ".exe" or ".dll" || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase);

            if (!wanted)
                continue;

            File.Copy(file, Path.Combine(targetDirectory, name), true);
        }
    }

    public static void Uninstall(bool deleteUserData)
    {
        var state = AppState.Load();

        foreach (ShortcutKind kind in Enum.GetValues<ShortcutKind>())
            Shortcuts.Remove(kind);

        try { WindowsIntegration.UnregisterPlayerProtocols(state); }
        catch (Exception ex) { Logger.WriteException("Uninstall::Protocols", ex); }

        try { WindowsIntegration.RemoveUninstall(); }
        catch (Exception ex) { Logger.WriteException("Uninstall::Registry", ex); }

        // The big stuff can go right now.
        foreach (string directory in new[] { Paths.Versions, Paths.Downloads, Paths.ModBackups })
            TryDelete(directory);

        TryDelete(Paths.StateFile);
        TryDelete(Paths.BootstrapperLock);

        if (deleteUserData)
        {
            TryDelete(Paths.Mods);
            TryDelete(Paths.SettingsFile);
        }
    }

    /// <summary>
    /// A running exe cannot delete itself, so a short-lived cmd removes the program files a couple of
    /// seconds later. Call this last, right before the app exits.
    /// </summary>
    public static void ScheduleSelfDelete(bool deleteUserData)
    {
        string command = deleteUserData
            ? $"/c ping 127.0.0.1 -n 3 > nul & rd /s /q \"{Paths.Base}\""
            : $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{Paths.Base}\\*.exe\" \"{Paths.Base}\\*.dll\" \"{Paths.Base}\\*.runtimeconfig.json\" \"{Paths.Base}\\*.deps.json\"";

        Process.Start(new ProcessStartInfo("cmd.exe", command)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Uninstall", $"Could not delete {path}: {ex.Message}");
        }
    }
}
