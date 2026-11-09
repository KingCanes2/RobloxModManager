using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;

namespace RobloxModManager.UI.Settings.Pages;

public partial class ShortcutsPage : UserControl, ISettingsPage
{
    private readonly SettingsContext _context;
    private readonly Dictionary<CheckBox, ShortcutKind> _toggles;

    public ShortcutsPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;

        _toggles = new Dictionary<CheckBox, ShortcutKind>
        {
            [DesktopToggle] = ShortcutKind.DesktopMenu,
            [StartMenuToggle] = ShortcutKind.StartMenuMenu,
            [PlayerToggle] = ShortcutKind.DesktopPlayer,
            [SettingsToggle] = ShortcutKind.DesktopSettings,
        };
    }

    public void OnShown()
    {
        foreach (var (toggle, kind) in _toggles)
            toggle.IsChecked = _context.ShortcutStates.TryGetValue(kind, out bool on) && on;
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox toggle && _toggles.TryGetValue(toggle, out ShortcutKind kind))
        {
            _context.ShortcutStates[kind] = toggle.IsChecked == true;
            _context.MarkChanged();
        }
    }
}
