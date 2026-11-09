using System.Windows;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Uninstaller;

public partial class UninstallerWindow : Window
{
    public UninstallerWindow()
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        Title = $"Uninstall {AppInfo.DisplayName}";
        HeadingText.Text = $"Uninstall {AppInfo.DisplayName}?";
        BodyText.Text =
            $"This removes {AppInfo.DisplayName} and the copy of Roblox it installed. " +
            "Roblox links will go back to the official Roblox player if it's installed; otherwise the Roblox website will offer to install it.";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (RobloxProcess.IsRunning())
        {
            MessageDialog.Warning("Close Roblox first", "Roblox is running from this installation. Close it, then try again.");
            return;
        }

        bool keepData = KeepDataBox.IsChecked == true;

        try
        {
            UninstallButton.IsEnabled = false;
            InstallService.Uninstall(deleteUserData: !keepData);
            Logger.WriteLine("Uninstaller", $"Uninstalled (kept data: {keepData})");
        }
        catch (Exception ex)
        {
            Logger.WriteException("Uninstaller", ex);
            MessageDialog.Error("Uninstall didn't finish", ex.Message, ex);
            UninstallButton.IsEnabled = true;
            return;
        }

        MessageDialog.Info("Uninstalled",
            keepData
                ? $"{AppInfo.DisplayName} has been removed. Your mods and settings are still in {Paths.Base}."
                : $"{AppInfo.DisplayName} has been removed.");

        try { InstallService.ScheduleSelfDelete(deleteUserData: !keepData); }
        catch (Exception ex) { Logger.WriteException("Uninstaller::SelfDelete", ex); }

        Close();
    }
}
