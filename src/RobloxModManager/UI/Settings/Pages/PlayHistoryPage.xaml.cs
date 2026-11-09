using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Settings.Pages;

public sealed record GameRow(long PlaceId, string Name, string Detail, double BarWidth);

public partial class PlayHistoryPage : UserControl, ISettingsPage
{
    private static readonly int[] ReminderMinutes = { 0, 30, 60, 90, 120, 180 };
    private const double BarMaxWidth = 220;

    private readonly SettingsContext _context;
    private bool _loading;
    private bool _lookingUp;

    public PlayHistoryPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
    }

    public void OnShown()
    {
        _loading = true;
        TrackToggle.IsChecked = _context.Settings.TrackPlaytime;
        int index = Array.IndexOf(ReminderMinutes, _context.Settings.BreakReminderMinutes);
        ReminderBox.SelectedIndex = index < 0 ? 0 : index;
        _loading = false;

        Refresh();
    }

    private void Refresh()
    {
        var history = PlayHistory.Load();
        var games = history.PerGame();

        WeekText.Text = PlayHistory.Format(history.TotalSince(DateTime.UtcNow.AddDays(-7)));
        TotalText.Text = PlayHistory.Format(games.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Total));
        int streak = history.DayStreak(DateTime.Now);
        StreakText.Text = streak.ToString(CultureInfo.CurrentCulture);

        double longest = games.Count > 0 ? Math.Max(games[0].Total.TotalSeconds, 1) : 1;

        GameList.ItemsSource = games.Select(g => new GameRow(
            g.PlaceId,
            g.Name,
            $"{PlayHistory.Format(g.Total)} over {g.Sessions} session{(g.Sessions == 1 ? "" : "s")}, last played {LastPlayed(g.LastPlayedUtc)}",
            Math.Max(4, BarMaxWidth * g.Total.TotalSeconds / longest))).ToList();

        EmptyText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = games.Count > 0;

        // Fill in names for games recorded while offline.
        var unnamed = games.Where(g => !history.GameNames.ContainsKey(g.PlaceId)).Select(g => g.PlaceId).Take(10).ToList();
        if (unnamed.Count > 0 && !_lookingUp)
            _ = LookUpNamesAsync(unnamed);
    }

    private static string LastPlayed(DateTime utc)
    {
        DateTime local = utc.ToLocalTime();
        if (local.Date == DateTime.Today) return "today";
        if (local.Date == DateTime.Today.AddDays(-1)) return "yesterday";
        return local.ToString("d MMM", CultureInfo.CurrentCulture);
    }

    private async Task LookUpNamesAsync(List<long> placeIds)
    {
        _lookingUp = true;
        bool found = false;

        try
        {
            foreach (long placeId in placeIds)
            {
                string? name = await RobloxGames.GetNameAsync(placeId);
                if (name is null)
                    continue;

                PlayHistory.SetName(placeId, name);
                found = true;
            }
        }
        finally
        {
            _lookingUp = false;
        }

        if (found && IsLoaded)
            Refresh();
    }

    private void Track_Click(object sender, RoutedEventArgs e)
    {
        _context.Settings.TrackPlaytime = TrackToggle.IsChecked == true;
        _context.MarkChanged();
    }

    private void Reminder_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ReminderBox.SelectedIndex < 0)
            return;

        _context.Settings.BreakReminderMinutes = ReminderMinutes[ReminderBox.SelectedIndex];
        _context.MarkChanged();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!MessageDialog.Confirm("Clear play history?", "This deletes all recorded play time. It can't be undone.",
                "Clear", "Cancel", DialogKind.Warning))
            return;

        try { PlayHistory.Clear(); }
        catch (Exception ex) { MessageDialog.Error("Couldn't clear history", ex.Message, ex); }

        Refresh();
    }

    private void OpenGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long placeId })
            return;

        try
        {
            Process.Start(new ProcessStartInfo($"https://www.roblox.com/games/{placeId}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageDialog.Error("Couldn't open the game page", ex.Message, ex);
        }
    }

    private void MakeProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long placeId })
            return;

        _context.PendingProfilePlaceId = placeId;
        _context.PendingProfileName = PlayHistory.Load().NameFor(placeId);
        _context.Navigate(SettingsPages.GameProfiles);
    }
}
