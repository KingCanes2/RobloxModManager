using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;
using RobloxModManager.UI.Bootstrapper;

namespace RobloxModManager.UI.Settings.Pages;

public partial class AppearancePage : UserControl, ISettingsPage
{
    private readonly SettingsContext _context;
    private bool _loading;

    public AppearancePage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
    }

    public void OnShown()
    {
        _loading = true;
        ThemeBox.SelectedIndex = (int)_context.Settings.Theme;
        StyleBox.SelectedIndex = (int)_context.Settings.BootstrapperStyle;
        TitleBox.Text = _context.Settings.BootstrapperTitle;
        _loading = false;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedIndex < 0)
            return;

        _context.Settings.Theme = (AppTheme)ThemeBox.SelectedIndex;
        ThemeManager.Apply(_context.Settings.Theme); // preview right away
        _context.MarkChanged();
    }

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StyleBox.SelectedIndex < 0)
            return;

        _context.Settings.BootstrapperStyle = (BootstrapperStyle)StyleBox.SelectedIndex;
        _context.MarkChanged();
    }

    private void Title_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;

        _context.Settings.BootstrapperTitle = string.IsNullOrWhiteSpace(TitleBox.Text) ? AppInfo.DisplayName : TitleBox.Text.Trim();
        _context.MarkChanged();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) =>
        BootstrapperWindow.ShowPreview(_context.Settings.BootstrapperStyle, _context.Settings.BootstrapperTitle, Window.GetWindow(this));
}
