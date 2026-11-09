using System.Windows;
using RobloxModManager.Core;

namespace RobloxModManager.UI.LaunchMenu;

public enum LaunchMenuChoice { None, LaunchRoblox, Settings, About }

/// <summary>Shown when the app is opened from its shortcut without arguments.</summary>
public partial class LaunchMenuWindow : Window
{
    public LaunchMenuChoice Choice { get; private set; } = LaunchMenuChoice.None;

    public LaunchMenuWindow()
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        Title = AppInfo.DisplayName;
        AppNameText.Text = AppInfo.DisplayName;
        VersionText.Text = $"Version {AppInfo.Version}";

        Loaded += (_, _) => LaunchButton.Focus();
    }

    private void Choose(LaunchMenuChoice choice)
    {
        Choice = choice;
        Close();
    }

    private void Launch_Click(object sender, RoutedEventArgs e) => Choose(LaunchMenuChoice.LaunchRoblox);

    private void Settings_Click(object sender, RoutedEventArgs e) => Choose(LaunchMenuChoice.Settings);

    private void About_Click(object sender, RoutedEventArgs e) => Choose(LaunchMenuChoice.About);
}
