using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Settings.Pages;

public sealed record ModFileItem(string Path, string Size);

public partial class ModsPage : UserControl, ISettingsPage
{
    private const string FlagsFile = "ClientSettings/ClientAppSettings.json";

    private readonly SettingsContext _context;

    public ModsPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
    }

    public void OnShown()
    {
        ApplyModsToggle.IsChecked = _context.Settings.ApplyMods;
        RefreshFont();
        RefreshList();
    }

    private void ApplyMods_Click(object sender, RoutedEventArgs e)
    {
        _context.Settings.ApplyMods = ApplyModsToggle.IsChecked == true;
        _context.MarkChanged();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Paths.Mods);
            Process.Start(new ProcessStartInfo(Paths.Mods) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageDialog.Error("Couldn't open the mods folder", ex.Message, ex);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        // Fast flags live in the mods folder too, but they're managed on their own page.
        var files = ModManager.Enumerate(Paths.Mods, relative => relative.Equals(FlagsFile, StringComparison.OrdinalIgnoreCase))
            .Select(file => new ModFileItem(file.Relative, FileSize.Format(file.Size)))
            .ToList();

        ModList.ItemsSource = files;
        ModList.Visibility = files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ModsEmptyText.Visibility = files.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshFont()
    {
        bool applied = CustomFont.IsApplied;

        FontOption.Description = applied
            ? "A custom font is in use. Remove it to go back to Roblox's own fonts."
            : "Replace the font used across Roblox's interface with a TrueType (.ttf) file.";

        RemoveFontButton.IsEnabled = applied;
    }

    private void ChooseFont_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a font",
            Filter = "TrueType fonts (*.ttf)|*.ttf"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            CustomFont.Apply(dialog.FileName);
            Logger.WriteLine("Mods", $"Custom font set from {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageDialog.Error("Couldn't use that font", ex.Message, ex);
        }

        RefreshFont();
        RefreshList();
    }

    private void RemoveFont_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CustomFont.Remove();
        }
        catch (Exception ex)
        {
            MessageDialog.Error("Couldn't remove the font", ex.Message, ex);
        }

        RefreshFont();
        RefreshList();
    }
}
