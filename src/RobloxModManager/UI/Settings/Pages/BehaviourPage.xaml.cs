using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;

namespace RobloxModManager.UI.Settings.Pages;

public partial class BehaviourPage : UserControl, ISettingsPage
{
    private readonly SettingsContext _context;
    private bool _loading;

    public BehaviourPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
    }

    public void OnShown()
    {
        _loading = true;

        var settings = _context.Settings;
        ConfirmToggle.IsChecked = settings.ConfirmLaunches;
        AgeBox.SelectedIndex = (int)settings.CleanerAge;
        CacheToggle.IsChecked = settings.CleanRobloxCache;
        RobloxLogsToggle.IsChecked = settings.CleanRobloxLogs;
        AppLogsToggle.IsChecked = settings.CleanAppLogs;
        UpdateCleanerEnabled();

        _loading = false;
    }

    private void UpdateCleanerEnabled()
    {
        bool on = _context.Settings.CleanerAge != CleanerAge.Never;
        CacheToggle.IsEnabled = on;
        RobloxLogsToggle.IsEnabled = on;
        AppLogsToggle.IsEnabled = on;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _context.Settings.ConfirmLaunches = ConfirmToggle.IsChecked == true;
        _context.MarkChanged();
    }

    private void Age_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AgeBox.SelectedIndex < 0)
            return;

        _context.Settings.CleanerAge = (CleanerAge)AgeBox.SelectedIndex;
        UpdateCleanerEnabled();
        _context.MarkChanged();
    }

    private void Cleaner_Click(object sender, RoutedEventArgs e)
    {
        _context.Settings.CleanRobloxCache = CacheToggle.IsChecked == true;
        _context.Settings.CleanRobloxLogs = RobloxLogsToggle.IsChecked == true;
        _context.Settings.CleanAppLogs = AppLogsToggle.IsChecked == true;
        _context.MarkChanged();
    }
}
