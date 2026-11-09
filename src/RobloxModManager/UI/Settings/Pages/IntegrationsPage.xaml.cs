using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RobloxModManager.Core;

namespace RobloxModManager.UI.Settings.Pages;

public partial class IntegrationsPage : UserControl
{
    private const string DefaultName = "New integration";

    private readonly SettingsContext _context;
    private bool _loading;

    public IntegrationsPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;

        IntegrationList.ItemsSource = _context.Settings.CustomIntegrations;
        ShowSelected();
    }

    private CustomIntegration? Selected => IntegrationList.SelectedItem as CustomIntegration;

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var integration = new CustomIntegration { Name = DefaultName };
        _context.Settings.CustomIntegrations.Add(integration);

        IntegrationList.Items.Refresh();
        IntegrationList.SelectedItem = integration;

        NameBox.Focus();
        NameBox.SelectAll();
        _context.MarkChanged();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } integration)
            return;

        _context.Settings.CustomIntegrations.Remove(integration);
        IntegrationList.Items.Refresh();
        IntegrationList.SelectedItem = null;
        _context.MarkChanged();
    }

    private void IntegrationList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    private void ShowSelected()
    {
        CustomIntegration? integration = Selected;

        EditorPanel.Visibility = integration is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = integration is null ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.IsEnabled = integration is not null;

        if (integration is null)
            return;

        _loading = true;

        // Only touch the text when it differs, so the caret doesn't jump while typing.
        SetText(NameBox, integration.Name);
        SetText(LocationBox, integration.Location);
        SetText(ArgsBox, integration.LaunchArgs);
        AutoCloseBox.IsChecked = integration.AutoClose;

        _loading = false;
    }

    private static void SetText(TextBox box, string value)
    {
        if (box.Text != value)
            box.Text = value;
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || Selected is not { } integration)
            return;

        bool renamed = integration.Name != NameBox.Text;

        integration.Name = NameBox.Text;
        integration.Location = LocationBox.Text.Trim().Trim('"');
        integration.LaunchArgs = ArgsBox.Text;
        integration.AutoClose = AutoCloseBox.IsChecked == true;

        if (renamed)
        {
            _loading = true;
            IntegrationList.Items.Refresh();
            IntegrationList.SelectedItem = integration;
            _loading = false;
        }

        _context.MarkChanged();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a program",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        LocationBox.Text = dialog.FileName;

        if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Text == DefaultName)
            NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }
}
