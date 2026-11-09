using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Settings.Pages;

public partial class DeploymentPage : UserControl, ISettingsPage
{
    private static readonly Regex ValidChannel = new(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant);

    private readonly SettingsContext _context;
    private bool _loading;
    private int _checkId;

    public DeploymentPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
    }

    public void OnShown()
    {
        var state = AppState.Load();
        bool installed = !string.IsNullOrEmpty(state.VersionGuid);

        InstalledVersionText.Text = installed ? (string.IsNullOrEmpty(state.Version) ? "Unknown" : state.Version) : "Not installed yet";
        InstalledGuidText.Text = installed ? state.VersionGuid : "Roblox downloads the first time you launch it.";

        _loading = true;
        ReinstallToggle.IsChecked = _context.ForceReinstall;
        ChannelBox.Text = _context.Settings.Channel;
        _loading = false;

        MarkChannelValid(true);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var state = AppState.Load();
        string folder = string.IsNullOrEmpty(state.VersionGuid) ? Paths.Versions : Paths.VersionDirectory(state.VersionGuid);

        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageDialog.Error("Couldn't open the folder", ex.Message, ex);
        }
    }

    private void Reinstall_Click(object sender, RoutedEventArgs e)
    {
        _context.ForceReinstall = ReinstallToggle.IsChecked == true;
        _context.MarkChanged();
    }

    private void MarkChannelValid(bool valid)
    {
        if (valid)
            ChannelBox.ClearValue(BorderBrushProperty);
        else
            ChannelBox.SetResourceReference(BorderBrushProperty, "DangerBrush");
    }

    private void Channel_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;

        string channel = ChannelBox.Text.Trim();

        if (channel.Length == 0)
        {
            MarkChannelValid(true);
            _context.Settings.Channel = Deployment.DefaultChannel;
            ChannelInfoText.Text = "Leave this empty to use production.";
            _context.MarkChanged();
            return;
        }

        bool valid = ValidChannel.IsMatch(channel);
        MarkChannelValid(valid);

        if (!valid)
        {
            ChannelInfoText.Text = "Channel names can only contain letters, numbers, dots, dashes and underscores.";
            return;
        }

        _context.Settings.Channel = channel;
        ChannelInfoText.Text = "";
        _context.MarkChanged();
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        string channel = _context.Settings.Channel;
        int id = ++_checkId;

        CheckButton.IsEnabled = false;
        ChannelInfoText.Text = $"Checking {channel}...";

        try
        {
            ClientVersion info = await new Deployment(channel).GetInfoAsync();

            if (id == _checkId)
                ChannelInfoText.Text = $"Latest on {channel}: {info.Version} ({info.VersionGuid})";
        }
        catch (ChannelNotFoundException ex)
        {
            if (id == _checkId)
                ChannelInfoText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.WriteException("Deployment::Check", ex);
            if (id == _checkId)
                ChannelInfoText.Text = "Couldn't reach Roblox. Check your internet connection and try again.";
        }
        finally
        {
            if (id == _checkId)
                CheckButton.IsEnabled = true;
        }
    }
}
