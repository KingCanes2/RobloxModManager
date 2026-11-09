using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;
using RobloxModManager.UI.Settings.Pages;

namespace RobloxModManager.UI.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsContext _context;
    private readonly Dictionary<string, Func<UserControl>> _factories;
    private readonly Dictionary<string, UserControl> _pages = new();
    private bool _syncingNav;
    private bool _closeConfirmed;

    /// <summary>True when the user chose "Save and launch Roblox".</summary>
    public bool LaunchAfterClose { get; private set; }

    public SettingsWindow(string? initialPage)
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        Title = $"{AppInfo.DisplayName} Settings";
        AppNameText.Text = AppInfo.DisplayName;

        _context = SettingsContext.Load();
        _context.NavigateRequested += Navigate;
        _context.Changed += UpdateStatus;

        _factories = new Dictionary<string, Func<UserControl>>
        {
            [SettingsPages.Integrations] = () => new IntegrationsPage(_context),
            [SettingsPages.Mods] = () => new ModsPage(_context),
            [SettingsPages.FastFlags] = () => new FastFlagsPage(_context),
            [SettingsPages.FastFlagEditor] = () => new FastFlagEditorPage(_context),
            [SettingsPages.EngineSettings] = () => new EngineSettingsPage(_context),
            [SettingsPages.Appearance] = () => new AppearancePage(_context),
            [SettingsPages.Behaviour] = () => new BehaviourPage(_context),
            [SettingsPages.Deployment] = () => new DeploymentPage(_context),
            [SettingsPages.Shortcuts] = () => new ShortcutsPage(_context),
            [SettingsPages.About] = () => new AboutPage(_context),
        };

        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;

        Navigate(initialPage is not null && _factories.ContainsKey(initialPage) ? initialPage : SettingsPages.Integrations);
        UpdateStatus();
    }

    private void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out UserControl? page))
        {
            page = _factories[key]();
            _pages[key] = page;
        }

        if (page is ISettingsPage settingsPage)
            settingsPage.OnShown();

        PageHost.Content = page;
        PageScroller.ScrollToTop();

        // The editor is a sub-page of Fast flags.
        string navKey = key == SettingsPages.FastFlagEditor ? SettingsPages.FastFlags : key;

        _syncingNav = true;
        NavList.SelectedItem = NavList.Items.OfType<ListBoxItem>().FirstOrDefault(item => (string)item.Tag == navKey);
        _syncingNav = false;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNav)
            return;

        if (NavList.SelectedItem is ListBoxItem { Tag: string key })
            Navigate(key);
    }

    private void UpdateStatus()
    {
        bool dirty = _context.IsDirty;
        StatusText.Text = dirty ? "You have unsaved changes." : "";
        SaveButton.IsEnabled = dirty;
    }

    private bool TrySave()
    {
        try
        {
            _context.Save();
            Logger.WriteLine("Settings", "Saved");
            return true;
        }
        catch (IOException ex) when (ex.Message.StartsWith("Your settings were saved", StringComparison.Ordinal))
        {
            MessageDialog.Warning("Shortcuts not updated", ex.Message);
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteException("Settings::Save", ex);
            MessageDialog.Error("Settings couldn't be saved", ex.Message, ex);
            return false;
        }
        finally
        {
            UpdateStatus();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TrySave())
            StatusText.Text = "Saved. Changes apply the next time Roblox starts.";
    }

    private void SaveLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySave())
            return;

        LaunchAfterClose = true;
        _closeConfirmed = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (_context.IsDirty && TrySave())
                StatusText.Text = "Saved. Changes apply the next time Roblox starts.";
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !_context.IsDirty)
            return;

        int choice = MessageDialog.ShowChoice("Save your changes?",
            "You've changed some settings. Save them before closing?",
            DialogKind.Question, null, "Save", "Don't save", "Cancel");

        if (choice == 0)
        {
            if (!TrySave())
                e.Cancel = true;
        }
        else if (choice != 1)
        {
            e.Cancel = true; // Cancel or the dialog was closed
        }
    }

    /// <summary>Lets a page close the window without the unsaved-changes prompt (used by Uninstall).</summary>
    public void CloseWithoutPrompt()
    {
        _closeConfirmed = true;
        Close();
    }
}
