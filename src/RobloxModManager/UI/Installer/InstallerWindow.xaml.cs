using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Installer;

public enum InstallerNextAction { None, LaunchRoblox, OpenSettings }

/// <summary>Three steps: welcome, location and shortcuts, done.</summary>
public partial class InstallerWindow : Window
{
    private enum Step { Welcome, Install, Done }

    private Step _step = Step.Welcome;
    private readonly bool _isUpdate;

    public bool Completed { get; private set; }
    public InstallerNextAction NextAction { get; private set; } = InstallerNextAction.None;

    public InstallerWindow(string suggestedDirectory, bool isUpdate)
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        _isUpdate = isUpdate;
        Title = $"{AppInfo.DisplayName} Setup";
        WelcomeTitle.Text = isUpdate ? $"Update {AppInfo.DisplayName}" : $"Welcome to {AppInfo.DisplayName}";
        DoneTitle.Text = isUpdate ? "Update complete" : "You're all set";
        LocationBox.Text = suggestedDirectory;

        if (isUpdate)
        {
            // Keep the existing location; moving an install is done by uninstalling first.
            LocationBox.IsEnabled = false;
            DesktopShortcutBox.IsChecked = Shortcuts.Exists(ShortcutKind.DesktopMenu);
            StartMenuShortcutBox.IsChecked = Shortcuts.Exists(ShortcutKind.StartMenuMenu);
        }

        ShowStep(Step.Welcome);
    }

    private void ShowStep(Step step)
    {
        _step = step;

        WelcomeStep.Visibility = step == Step.Welcome ? Visibility.Visible : Visibility.Collapsed;
        InstallStep.Visibility = step == Step.Install ? Visibility.Visible : Visibility.Collapsed;
        DoneStep.Visibility = step == Step.Done ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = step == Step.Install ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = step == Step.Done ? Visibility.Visible : Visibility.Collapsed;

        NextButton.Content = step switch
        {
            Step.Install => _isUpdate ? "Update" : "Install",
            Step.Done => "Launch Roblox",
            _ => "Next"
        };

        CancelButton.Content = step == Step.Done ? "Close" : "Cancel";

        if (step == Step.Install)
            LocationBox.Focus();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(Step.Welcome);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        NextAction = InstallerNextAction.OpenSettings;
        Close();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case Step.Welcome:
                ShowStep(Step.Install);
                break;

            case Step.Install:
                Install();
                break;

            case Step.Done:
                NextAction = InstallerNextAction.LaunchRoblox;
                Close();
                break;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder for Roblox Mod Manager",
            InitialDirectory = Directory.Exists(LocationBox.Text) ? LocationBox.Text : Paths.LocalAppData
        };

        if (dialog.ShowDialog(this) != true)
            return;

        string folder = dialog.FolderName;

        // Picking "D:\Games" should install to "D:\Games\RobloxModManager", not spill files into D:\Games.
        if (!string.Equals(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)), AppInfo.ProjectName, StringComparison.OrdinalIgnoreCase))
            folder = Path.Combine(folder, AppInfo.ProjectName);

        LocationBox.Text = folder;
    }

    private void Location_TextChanged(object sender, TextChangedEventArgs e) => LocationError.Visibility = Visibility.Collapsed;

    private string? ValidateLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return "Choose a folder to install to.";

        if (!Path.IsPathFullyQualified(location))
            return "Enter a full folder path, such as C:\\Users\\you\\AppData\\Local\\RobloxModManager.";

        if (location.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "That folder path contains characters Windows doesn't allow.";

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (string protectedRoot in new[] { programFiles, programFilesX86 })
        {
            if (!string.IsNullOrEmpty(protectedRoot)
                && Path.GetFullPath(location).StartsWith(protectedRoot, StringComparison.OrdinalIgnoreCase))
                return "Program Files needs administrator rights to write to. Choose a folder in your user profile instead.";
        }

        if (!_isUpdate && Directory.Exists(location) && Directory.EnumerateFileSystemEntries(location).Any()
            && !File.Exists(Path.Combine(location, AppInfo.ExecutableName))
            && !File.Exists(Path.Combine(location, "Settings.json")))
            return "That folder already has other files in it. Choose an empty folder.";

        return null;
    }

    private void Install()
    {
        string location = LocationBox.Text.Trim();
        string? error = ValidateLocation(location);

        if (error is not null)
        {
            LocationError.Text = error;
            LocationError.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            NextButton.IsEnabled = false;
            InstallService.Install(location, DesktopShortcutBox.IsChecked == true, StartMenuShortcutBox.IsChecked == true);

            Completed = true;
            Logger.WriteLine("Installer", $"Installed to {location}");
            ShowStep(Step.Done);
        }
        catch (IOException ex) when (_isUpdate)
        {
            Logger.WriteException("Installer", ex);
            MessageDialog.Error("Couldn't update",
                $"The installed copy of {AppInfo.DisplayName} is in use. Close its settings window and Roblox, then try again.", ex);
        }
        catch (Exception ex)
        {
            Logger.WriteException("Installer", ex);
            MessageDialog.Error("Installation failed", ex.Message, ex);
        }
        finally
        {
            NextButton.IsEnabled = true;
        }
    }
}
