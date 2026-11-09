using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Settings.Pages;

public partial class AboutPage : UserControl
{
    private readonly SettingsContext _context;

    public AboutPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;

        AppNameText.Text = AppInfo.DisplayName;
        VersionText.Text = $"Version {AppInfo.Version}";
        UninstallOption.Description = $"Remove {AppInfo.DisplayName} and the copy of Roblox it installed.";
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageDialog.Error("Couldn't open that", ex.Message, ex);
        }
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && url.StartsWith("https://", StringComparison.Ordinal))
            Open(url);
    }

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Paths.Logs);
        Open(Paths.Logs);
    }

    private void InstallFolder_Click(object sender, RoutedEventArgs e) => Open(Paths.Base);

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_context.IsDirty && !MessageDialog.Confirm("Unsaved changes",
                "You have unsaved changes. They'll be lost if you uninstall. Continue?", "Continue", "Cancel", DialogKind.Warning))
            return;

        try
        {
            // The uninstaller runs as its own process so this window can close cleanly first.
            Process.Start(new ProcessStartInfo(Paths.Application, "-uninstall") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            MessageDialog.Error("Couldn't start the uninstaller",
                $"You can also uninstall from Windows Settings > Apps.\n\n{ex.Message}", ex);
            return;
        }

        if (Window.GetWindow(this) is SettingsWindow window)
            window.CloseWithoutPrompt();
    }
}
