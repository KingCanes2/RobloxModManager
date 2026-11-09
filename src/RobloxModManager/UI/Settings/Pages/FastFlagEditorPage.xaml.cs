using System.Windows;
using System.Windows.Controls;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Settings.Pages;

/// <summary>One row in the editor. Editing <see cref="Value"/> writes straight into the flag set.</summary>
public sealed class FlagRow
{
    private readonly SettingsContext _context;
    private string _value;

    public FlagRow(SettingsContext context, string name, string value)
    {
        _context = context;
        Name = name;
        _value = value;
    }

    public string Name { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;

            _value = value;
            _context.Flags.Set(Name, value);
            _context.MarkChanged();
        }
    }
}

public partial class FastFlagEditorPage : UserControl, ISettingsPage
{
    private readonly SettingsContext _context;

    public FastFlagEditorPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
    }

    public void OnShown() => Refresh();

    private void Refresh(IEnumerable<string>? select = null)
    {
        string filter = SearchBox.Text.Trim();

        var rows = _context.Flags.Flags
            .Where(pair => filter.Length == 0 || pair.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FlagRow(_context, pair.Key, pair.Value))
            .ToList();

        FlagList.ItemsSource = rows;

        if (select is not null)
        {
            var wanted = new HashSet<string>(select, StringComparer.Ordinal);
            foreach (FlagRow row in rows.Where(r => wanted.Contains(r.Name)))
                FlagList.SelectedItems.Add(row);

            if (FlagList.SelectedItems.Count > 0)
                FlagList.ScrollIntoView(FlagList.SelectedItems[0]!);
        }

        int total = _context.Flags.Flags.Count;
        CountText.Text = total == 0
            ? "No flags yet. Choose Add, or Import JSON to paste a list."
            : filter.Length > 0 ? $"Showing {rows.Count} of {total} flags." : $"{total} flag{(total == 1 ? "" : "s")}.";

        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.IsEnabled = FlagList.SelectedItems.Count > 0;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _context.Navigate(SettingsPages.FastFlags);

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void FlagList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DeleteButton.IsEnabled = FlagList.SelectedItems.Count > 0;

    private void Add_Click(object sender, RoutedEventArgs e) => ShowAddDialog(import: false);

    private void Import_Click(object sender, RoutedEventArgs e) => ShowAddDialog(import: true);

    private void ShowAddDialog(bool import)
    {
        var dialog = new AddFastFlagDialog(_context.Flags.Flags, import) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() != true || dialog.Result.Count == 0)
            return;

        foreach (var (name, value) in dialog.Result)
            _context.Flags.Set(name, value);

        // Make sure the new flags are visible even if a search was active.
        SearchBox.Text = "";
        Refresh(dialog.Result.Keys);
        _context.MarkChanged();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = FlagList.SelectedItems.OfType<FlagRow>().ToList();
        if (selected.Count == 0)
            return;

        if (selected.Count > 1 && !MessageDialog.Confirm("Delete flags?",
                $"Delete {selected.Count} flags? Nothing changes until you save.", "Delete", "Cancel", DialogKind.Warning))
            return;

        foreach (FlagRow row in selected)
            _context.Flags.Set(row.Name, null);

        Refresh();
        _context.MarkChanged();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_context.Flags.ToJson());
            CountText.Text = "Copied all flags to the clipboard as JSON.";
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another program for a moment.
            MessageDialog.Error("Couldn't copy", "Another program is using the clipboard. Try again.", ex);
        }
    }
}
