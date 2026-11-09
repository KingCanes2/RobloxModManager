using System.Diagnostics;
using System.Net.Sockets;
using RobloxModManager.Core;
using RobloxModManager.UI.Bootstrapper;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI;

/// <summary>Everything that happens between "launch Roblox" and Roblox being on screen.</summary>
public static class LaunchFlow
{
    public static async Task RunPlayerAsync(LaunchArguments args)
    {
        var settings = AppSettings.Load();

        if (args.RejectedLink)
        {
            MessageDialog.Warning("Game link not used",
                "The link that opened Roblox Mod Manager contained characters that aren't allowed, so Roblox will open on its home screen instead.");
        }

        if (settings.ConfirmLaunches && RobloxProcess.IsRunning()
            && !MessageDialog.Confirm("Roblox is already open",
                "Opening Roblox again will close the game you're in now. Continue?",
                "Continue", "Cancel", DialogKind.Warning))
        {
            return;
        }

        var window = new BootstrapperWindow(settings.BootstrapperStyle, settings.BootstrapperTitle);
        window.Show();

        RobloxBootstrapper? bootstrapper = null;
        string? executable = null;

        try
        {
            for (int attempt = 0; attempt < 2 && executable is null; attempt++)
            {
                bootstrapper?.Dispose();
                bootstrapper = new RobloxBootstrapper(settings, AppState.Load(), new Deployment(settings.Channel), window);

                RobloxBootstrapper current = bootstrapper;
                Action cancel = current.Cancel;
                window.CancelRequested += cancel;

                try
                {
                    // Run off the UI thread so hashing and extraction never freeze the window.
                    executable = await Task.Run(() => current.PrepareAsync());
                }
                catch (OperationCanceledException)
                {
                    Logger.WriteLine("LaunchFlow", "Cancelled by the user");
                    return;
                }
                catch (ChannelNotFoundException ex) when (attempt == 0)
                {
                    bool reset = MessageDialog.Confirm("Channel not available",
                        $"{ex.Message}\n\nSwitch back to the standard channel (production) and continue?",
                        "Switch to production", "Cancel", DialogKind.Warning);

                    if (!reset)
                        return;

                    settings.Channel = Deployment.DefaultChannel;
                    settings.Save();
                }
                finally
                {
                    window.CancelRequested -= cancel;
                }
            }

            if (executable is null || bootstrapper is null)
                return;

            Process roblox = bootstrapper.StartRoblox(LaunchArguments.RefreshLaunchTime(args.RobloxArguments));

            var integrations = new IntegrationRunner();
            integrations.StartAll(settings.CustomIntegrations);

            // Give Roblox a moment to show its window before ours disappears.
            await Task.Delay(1500);
            window.Finish();

            await Task.Run(() => Cleaner.Run(settings));

            if (integrations.HasAutoClose)
            {
                try { await RobloxProcess.WaitForExitAsync(roblox, CancellationToken.None); }
                catch (Exception ex) { Logger.WriteException("LaunchFlow::Wait", ex); }

                integrations.CloseAutoClose();
            }
        }
        catch (Exception ex)
        {
            Logger.WriteException("LaunchFlow", ex);
            window.Finish();
            ShowLaunchError(ex);
        }
        finally
        {
            if (window.IsVisible)
                window.Finish();

            bootstrapper?.Dispose();
        }
    }

    private static void ShowLaunchError(Exception ex)
    {
        switch (ex)
        {
            case BootstrapperException:
                MessageDialog.Error("Roblox couldn't be started", ex.Message, ex);
                break;

            case ChannelNotFoundException:
                MessageDialog.Error("Channel not available", ex.Message + " You can change the channel under Settings > Deployment.", ex);
                break;

            case ChecksumFailedException:
                MessageDialog.Error("Download failed",
                    "A Roblox file kept arriving damaged. This is usually caused by an unstable connection, a VPN or antivirus software. Try again in a few minutes.", ex);
                break;

            case HttpRequestException or TimeoutException or SocketException:
                MessageDialog.Error("Couldn't connect to Roblox",
                    "Check your internet connection and try again. If you use a VPN, proxy or firewall, make sure it allows Roblox.", ex);
                break;

            case UnauthorizedAccessException or IOException:
                MessageDialog.Error("Couldn't write Roblox's files",
                    $"{ex.Message}\n\nClose Roblox if it's open, and check that your antivirus isn't blocking {AppInfo.DisplayName}.", ex);
                break;

            default:
                MessageDialog.Error("Something went wrong", ex.Message, ex);
                break;
        }
    }
}
