using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RobloxModManager.Core;

namespace RobloxModManager.UI;

/// <summary>Switches between the light and dark colour dictionaries and keeps window title bars in sync.</summary>
public static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19; // Windows 10 before 20H1

    public static bool IsDark { get; private set; } = true;

    public static void Apply(AppTheme theme)
    {
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => !SystemUsesLightTheme()
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var colours = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/UI/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
        };

        if (dictionaries.Count > 0)
            dictionaries[0] = colours;
        else
            dictionaries.Insert(0, colours);

        foreach (Window window in Application.Current.Windows)
            ApplyTitleBar(window);
    }

    /// <summary>Call from a window's constructor so its title bar matches the theme.</summary>
    public static void Attach(Window window) => window.SourceInitialized += (_, _) => ApplyTitleBar(window);

    private static bool SystemUsesLightTheme()
    {
        try { return WindowsIntegration.AppsUseLightTheme(); }
        catch { return false; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void ApplyTitleBar(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            int dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeLegacy, ref dark, sizeof(int));
        }
        catch
        {
            // purely cosmetic
        }
    }
}
