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

    // ------------------------------------------------------------------ smart drop

    private void SetDropHighlight(bool on)
    {
        if (on)
            DropZone.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "AccentBrush");
        else
            DropZone.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlBorderBrush");
    }

    private void Drop_DragEnter(object sender, DragEventArgs e) => Drop_DragOver(sender, e);

    private void Drop_DragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHighlight(files);
        e.Handled = true;
    }

    private void Drop_DragLeave(object sender, DragEventArgs e) => SetDropHighlight(false);

    private void Drop_Drop(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            PlaceFiles(paths);
    }

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose mod files",
            Multiselect = true,
            Filter = "Mod files|*.ogg;*.mp3;*.wav;*.png;*.jpg;*.jpeg;*.ttf;*.otf;*.zip;*.json;*.mesh;*.rbxm|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            PlaceFiles(dialog.FileNames);
    }

    private void PlaceFiles(IEnumerable<string> paths)
    {
        var state = AppState.Load();
        string versionDirectory = string.IsNullOrEmpty(state.VersionGuid) ? "" : Paths.VersionDirectory(state.VersionGuid);

        if (versionDirectory.Length == 0 || !Directory.Exists(versionDirectory))
        {
            MessageDialog.Info("Launch Roblox once first",
                "Smart drop matches your files against Roblox's own files, so Roblox needs to be installed. Launch it once, then drop your mods again.");
            return;
        }

        var index = ModPlacer.IndexVersion(versionDirectory);
        var results = new List<Placement>();

        // Expand dropped folders into their files.
        var files = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
                files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories));
            else if (File.Exists(path))
                files.Add(path);
        }

        foreach (string file in files)
        {
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try { results.AddRange(ModPlacer.PlaceZip(file, index, Paths.Mods)); }
                catch (Exception ex) { results.Add(new Placement(file, PlacementOutcome.Failed, null, Array.Empty<string>(), ex.Message)); }
                continue;
            }

            if (file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) && !index.ContainsKey(Path.GetFileName(file)))
            {
                // A font Roblox doesn't have: most likely meant as the custom font.
                if (MessageDialog.Confirm("Use as custom font?",
                        $"Roblox has no font called {Path.GetFileName(file)}. Use it as the font for all of Roblox's interface instead?",
                        "Use as custom font", "Skip"))
                {
                    try
                    {
                        CustomFont.Apply(file);
                        results.Add(new Placement(file, PlacementOutcome.Placed, "custom font", Array.Empty<string>(), null));
                    }
                    catch (Exception ex)
                    {
                        results.Add(new Placement(file, PlacementOutcome.Failed, null, Array.Empty<string>(), ex.Message));
                    }
                }
                continue;
            }

            var placement = ModPlacer.PlaceFile(file, index, Paths.Mods);

            if (placement.Outcome == PlacementOutcome.Ambiguous)
            {
                var options = placement.Candidates.Take(3).ToList();
                var buttons = options.Append("Skip").ToArray();

                int choice = MessageDialog.ShowChoice($"Where should {Path.GetFileName(file)} go?",
                    $"{placement.Message} Pick the one to replace. The first is the most likely.",
                    DialogKind.Question, null, buttons);

                placement = choice >= 0 && choice < options.Count
                    ? ModPlacer.CopyTo(file, options[choice], Paths.Mods)
                    : placement with { Message = "Skipped." };
            }

            results.Add(placement);
        }

        RefreshFont();
        RefreshList();
        ShowSummary(results);
    }

    private static void ShowSummary(List<Placement> results)
    {
        if (results.Count == 0)
            return;

        var placed = results.Where(r => r.Outcome == PlacementOutcome.Placed).ToList();
        var notPlaced = results.Where(r => r.Outcome != PlacementOutcome.Placed).ToList();

        var lines = new List<string>();
        lines.AddRange(placed.Take(8).Select(r => $"✓ {Path.GetFileName(r.Source)} → {r.RelativePath}"));
        if (placed.Count > 8)
            lines.Add($"   and {placed.Count - 8} more");
        lines.AddRange(notPlaced.Take(6).Select(r => $"✗ {Path.GetFileName(r.Source)}: {r.Message}"));
        if (notPlaced.Count > 6)
            lines.Add($"   and {notPlaced.Count - 6} more not added");

        string heading = notPlaced.Count == 0
            ? $"Added {placed.Count} mod file{(placed.Count == 1 ? "" : "s")}"
            : placed.Count == 0 ? "No files were added" : $"Added {placed.Count} of {results.Count} files";

        MessageDialog.ShowChoice(heading,
            string.Join("\n", lines) + "\n\nThey'll be applied the next time Roblox starts.",
            notPlaced.Count == 0 ? DialogKind.Info : DialogKind.Warning, null, "OK");
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
