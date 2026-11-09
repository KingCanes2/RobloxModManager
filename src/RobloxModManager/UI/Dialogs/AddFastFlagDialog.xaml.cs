using System.Text.RegularExpressions;
using System.Windows;
using RobloxModManager.Core;

namespace RobloxModManager.UI.Dialogs;

/// <summary>Adds one flag, or imports many from JSON.</summary>
public partial class AddFastFlagDialog : Window
{
    private static readonly Regex ValidName = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    // Roblox flag names start with one of these; anything else is almost certainly a typo.
    private static readonly string[] KnownPrefixes =
    {
        "FFlag", "DFFlag", "SFFlag", "FInt", "DFInt", "FString", "DFString", "FLog", "DFLog"
    };

    private readonly IReadOnlyDictionary<string, string> _existing;
    private readonly bool _import;

    /// <summary>Flags to add. Empty if the dialog was cancelled.</summary>
    public Dictionary<string, string> Result { get; } = new(StringComparer.Ordinal);

    public AddFastFlagDialog(IReadOnlyDictionary<string, string> existing, bool import)
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        _existing = existing;
        _import = import;

        Title = import ? "Import fast flags" : "Add a fast flag";
        HeadingText.Text = Title;
        OkButton.Content = import ? "Import" : "Add";

        SinglePanel.Visibility = import ? Visibility.Collapsed : Visibility.Visible;
        ImportPanel.Visibility = import ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) => (import ? JsonBox : NameBox).Focus();
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => ErrorText.Visibility = Visibility.Collapsed;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static bool LooksLikeFlag(string name) => KnownPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_import)
        {
            if (string.IsNullOrWhiteSpace(JsonBox.Text))
            {
                ShowError("Paste some JSON first.");
                return;
            }

            if (!FastFlagManager.TryParse(JsonBox.Text, out var parsed, out string? error))
            {
                ShowError($"That isn't valid JSON: {error}");
                return;
            }

            if (parsed.Count == 0)
            {
                ShowError("No flags were found in that JSON.");
                return;
            }

            var invalid = parsed.Keys.Where(name => !ValidName.IsMatch(name)).ToList();
            if (invalid.Count > 0)
            {
                ShowError($"These names aren't valid flag names: {string.Join(", ", invalid.Take(5))}{(invalid.Count > 5 ? "..." : "")}");
                return;
            }

            foreach (var (name, value) in parsed)
                candidates[name] = value;
        }
        else
        {
            string name = NameBox.Text.Trim();
            string value = ValueBox.Text.Trim();

            if (name.Length == 0)
            {
                ShowError("Enter the flag's name.");
                return;
            }

            if (!ValidName.IsMatch(name))
            {
                ShowError("Flag names can only contain letters, numbers and underscores, and can't start with a number.");
                return;
            }

            if (value.Length == 0)
            {
                ShowError("Enter a value. To remove a flag, delete it from the list instead.");
                return;
            }

            candidates[name] = value;
        }

        var unusual = candidates.Keys.Where(name => !LooksLikeFlag(name)).ToList();
        if (unusual.Count > 0 && !MessageDialog.Confirm("Unusual flag name",
                $"{string.Join(", ", unusual.Take(5))} {(unusual.Count == 1 ? "doesn't" : "don't")} start with a known prefix such as FFlag or DFInt, so Roblox will probably ignore {(unusual.Count == 1 ? "it" : "them")}. Add anyway?",
                "Add anyway", "Go back", DialogKind.Warning))
            return;

        var conflicts = candidates.Where(pair => _existing.TryGetValue(pair.Key, out string? old) && old != pair.Value).ToList();
        if (conflicts.Count > 0)
        {
            string list = string.Join("\n", conflicts.Take(5).Select(pair => $"{pair.Key}: {_existing[pair.Key]} → {pair.Value}"));

            int choice = MessageDialog.ShowChoice("Replace existing flags?",
                $"{conflicts.Count} flag{(conflicts.Count == 1 ? " is" : "s are")} already set to a different value:\n\n{list}{(conflicts.Count > 5 ? "\n..." : "")}",
                DialogKind.Question, null, "Replace", "Keep existing", "Cancel");

            if (choice == 1)
                foreach (var pair in conflicts)
                    candidates.Remove(pair.Key);
            else if (choice != 0)
                return;
        }

        foreach (var (name, value) in candidates)
            Result[name] = value;

        DialogResult = true;
    }
}
