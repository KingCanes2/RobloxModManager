using System.Text.Json;
using RobloxModManager.Core;

namespace RobloxModManager.UI.Settings;

/// <summary>Page keys used for navigation.</summary>
public static class SettingsPages
{
    public const string Integrations = "Integrations";
    public const string GameProfiles = "GameProfiles";
    public const string PlayHistory = "PlayHistory";
    public const string Mods = "Mods";
    public const string FastFlags = "FastFlags";
    public const string FastFlagEditor = "FastFlagEditor";
    public const string EngineSettings = "EngineSettings";
    public const string Appearance = "Appearance";
    public const string Behaviour = "Behaviour";
    public const string Deployment = "Deployment";
    public const string Shortcuts = "Shortcuts";
    public const string About = "About";
}

/// <summary>Implemented by pages that need to refresh when shown again (another page may have changed shared data).</summary>
public interface ISettingsPage
{
    void OnShown();
}

/// <summary>
/// Everything the settings pages edit. Nothing is written to disk until <see cref="Save"/>,
/// except the custom font and files in the mods folder, which apply immediately.
/// </summary>
public sealed class SettingsContext
{
    public AppSettings Settings { get; }
    public FastFlagManager Flags { get; } = new();
    public GlobalSettingsManager Global { get; } = new();

    public bool GlobalReadOnly { get; set; }
    public bool GlobalDirty { get; set; }
    public bool ForceReinstall { get; set; }
    public Dictionary<ShortcutKind, bool> ShortcutStates { get; } = new();

    /// <summary>Set by Play history's "Make profile"; picked up by the Game profiles page.</summary>
    public long? PendingProfilePlaceId { get; set; }
    public string? PendingProfileName { get; set; }

    private string _savedSettings = "";
    private string _savedFlags = "";
    private bool _savedGlobalReadOnly;
    private bool _savedForceReinstall;
    private Dictionary<ShortcutKind, bool> _savedShortcuts = new();

    public event Action<string>? NavigateRequested;
    public event Action? Changed;

    private SettingsContext(AppSettings settings) => Settings = settings;

    public static SettingsContext Load()
    {
        var context = new SettingsContext(AppSettings.Load());

        context.Flags.Load();
        context.Global.Load();
        context.GlobalReadOnly = context.Global.IsReadOnly;
        context.ForceReinstall = AppState.Load().ForceReinstall;

        foreach (ShortcutKind kind in Enum.GetValues<ShortcutKind>())
            context.ShortcutStates[kind] = Shortcuts.Exists(kind);

        context.Snapshot();
        return context;
    }

    private void Snapshot()
    {
        _savedSettings = JsonSerializer.Serialize(Settings, JsonFile.Options);
        _savedFlags = Flags.ToJson();
        _savedGlobalReadOnly = GlobalReadOnly;
        _savedForceReinstall = ForceReinstall;
        _savedShortcuts = new Dictionary<ShortcutKind, bool>(ShortcutStates);
    }

    public bool IsDirty =>
        JsonSerializer.Serialize(Settings, JsonFile.Options) != _savedSettings
        || Flags.ToJson() != _savedFlags
        || GlobalDirty
        || GlobalReadOnly != _savedGlobalReadOnly
        || ForceReinstall != _savedForceReinstall
        || ShortcutStates.Any(pair => !_savedShortcuts.TryGetValue(pair.Key, out bool saved) || saved != pair.Value);

    public void MarkChanged() => Changed?.Invoke();

    public void Navigate(string page) => NavigateRequested?.Invoke(page);

    public void Save()
    {
        Settings.Save();
        Flags.Save();

        if (Global.Loaded && (GlobalDirty || GlobalReadOnly != _savedGlobalReadOnly))
            Global.Save(GlobalReadOnly);

        if (ForceReinstall != _savedForceReinstall)
        {
            var state = AppState.Load();
            state.ForceReinstall = ForceReinstall;
            state.Save();
        }

        var shortcutErrors = new List<string>();

        // Iterate a copy: a failed shortcut is reset to its real state inside the loop.
        foreach (var (kind, wanted) in ShortcutStates.ToList())
        {
            try { Shortcuts.Set(kind, wanted); }
            catch (Exception ex)
            {
                Logger.WriteException("Settings::Shortcuts", ex);
                shortcutErrors.Add(ex.Message);
                ShortcutStates[kind] = Shortcuts.Exists(kind);
            }
        }

        GlobalDirty = false;
        Snapshot();
        Changed?.Invoke();

        if (shortcutErrors.Count > 0)
            throw new IOException("Your settings were saved, but some shortcuts couldn't be updated: " + shortcutErrors[0]);
    }
}
