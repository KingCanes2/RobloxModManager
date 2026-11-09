using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;

namespace RobloxModManager.UI.Settings.Pages;

public partial class GameProfilesPage : UserControl, ISettingsPage
{
    private readonly SettingsContext _context;
    private bool _loading;
    private int _lookupId;

    private List<GameProfile> Profiles => _context.Settings.GameProfiles;

    public GameProfilesPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
        ProfileList.ItemsSource = Profiles;
    }

    private GameProfile? Selected => ProfileList.SelectedItem as GameProfile;

    public void OnShown()
    {
        ProfileList.Items.Refresh();

        // Coming from Play history's "Make profile" button.
        if (_context.PendingProfilePlaceId is long placeId)
        {
            _context.PendingProfilePlaceId = null;
            string name = _context.PendingProfileName ?? $"Place {placeId}";
            _context.PendingProfileName = null;

            GameProfile? existing = Profiles.FirstOrDefault(p => p.PlaceId == placeId);
            if (existing is null)
            {
                existing = new GameProfile { PlaceId = placeId, Name = name };
                Profiles.Add(existing);
                ProfileList.Items.Refresh();
                _context.MarkChanged();
            }

            ProfileList.SelectedItem = existing;
        }

        ShowSelected();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var profile = new GameProfile { Name = "New profile" };
        Profiles.Add(profile);
        ProfileList.Items.Refresh();
        ProfileList.SelectedItem = profile;
        PlaceBox.Focus();
        _context.MarkChanged();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile)
            return;

        Profiles.Remove(profile);
        ProfileList.Items.Refresh();
        ProfileList.SelectedItem = null;
        _context.MarkChanged();
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            ShowSelected();
    }

    private void ShowSelected()
    {
        GameProfile? profile = Selected;

        EditorPanel.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = profile is null ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.IsEnabled = profile is not null;

        if (profile is null)
            return;

        _loading = true;
        PlaceBox.Text = profile.PlaceId > 0 ? profile.PlaceId.ToString() : "";
        NameBox.Text = profile.Name;
        ModsBox.IsChecked = profile.ApplyMods;
        FlagsBox.Text = profile.Flags.Count == 0
            ? ""
            : System.Text.Json.JsonSerializer.Serialize(new SortedDictionary<string, string>(profile.Flags), JsonFile.Options);
        _loading = false;

        UpdatePlaceHint();
        UpdateFlagsStatus(profile.Flags.Count, null);
    }

    private void UpdatePlaceHint()
    {
        long? placeId = RobloxGames.ParsePlaceInput(PlaceBox.Text);

        if (PlaceBox.Text.Trim().Length == 0)
            PlaceHint.Text = "Paste the game's page link, or its place ID.";
        else if (placeId is null)
            PlaceHint.Text = "That doesn't look like a Roblox game link or place ID.";
        else if (Profiles.Any(p => p != Selected && p.PlaceId == placeId))
            PlaceHint.Text = $"Another profile already uses place {placeId}; only the first one will be used.";
        else
            PlaceHint.Text = $"Place ID {placeId}.";

        LookUpButton.IsEnabled = placeId is not null;
    }

    private void Place_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected is not { } profile)
            return;

        long? placeId = RobloxGames.ParsePlaceInput(PlaceBox.Text);
        profile.PlaceId = placeId ?? 0;
        UpdatePlaceHint();
        _context.MarkChanged();
    }

    private async void LookUp_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile || profile.PlaceId <= 0)
            return;

        int id = ++_lookupId;
        LookUpButton.IsEnabled = false;
        PlaceHint.Text = "Looking up the game...";

        string? name = await RobloxGames.GetNameAsync(profile.PlaceId);

        if (id != _lookupId)
            return;

        LookUpButton.IsEnabled = true;

        if (name is null)
        {
            PlaceHint.Text = "Couldn't find that game. Check the link, or type a name yourself.";
            return;
        }

        PlaceHint.Text = $"Place ID {profile.PlaceId}: {name}";
        if (Selected == profile)
            NameBox.Text = name; // triggers Name_TextChanged, which saves it
    }

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected is not { } profile)
            return;

        profile.Name = NameBox.Text;

        _loading = true;
        ProfileList.Items.Refresh();
        ProfileList.SelectedItem = profile;
        _loading = false;

        _context.MarkChanged();
    }

    private void Mods_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile)
            return;

        profile.ApplyMods = ModsBox.IsChecked == true;
        _context.MarkChanged();
    }

    private void Flags_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected is not { } profile)
            return;

        if (FlagsBox.Text.Trim().Length == 0)
        {
            profile.Flags = new Dictionary<string, string>(StringComparer.Ordinal);
            UpdateFlagsStatus(0, null);
            _context.MarkChanged();
            return;
        }

        if (FastFlagManager.TryParse(FlagsBox.Text, out var flags, out string? error))
        {
            profile.Flags = flags;
            UpdateFlagsStatus(flags.Count, null);
            _context.MarkChanged();
        }
        else
        {
            // Keep the last valid flags until the JSON is fixed.
            UpdateFlagsStatus(profile.Flags.Count, error);
        }
    }

    private void UpdateFlagsStatus(int count, string? error)
    {
        if (error is not null)
        {
            FlagsStatus.Text = $"Not valid JSON yet, so the last valid version is kept. ({error})";
            FlagsStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            return;
        }

        FlagsStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        FlagsStatus.Text = count == 0 ? "No extra flags." : $"{count} extra flag{(count == 1 ? "" : "s")}.";
    }
}
