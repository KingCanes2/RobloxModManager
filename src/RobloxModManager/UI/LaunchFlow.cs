using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Threading;
using RobloxModManager.Core;
using RobloxModManager.UI.Bootstrapper;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI;

/// <summary>Everything that happens between "launch Roblox" and Roblox closing again.</summary>
public static class LaunchFlow
{
    public static async Task RunPlayerAsync(LaunchArguments args)
    {
        var settings = AppSettings.Load();

        // Shift held while launching = one launch without mods or fast flags.
        bool safeMode = SafeMode.IsShiftHeld();
        AppSettings launchSettings = settings;

        if (safeMode)
        {
            launchSettings = settings.Clone();
            launchSettings.ApplyMods = false;
            launchSettings.UseFastFlagManager = false;
            Logger.WriteLine("LaunchFlow", "Safe mode: Shift was held, skipping mods and fast flags");
        }

        long? placeId = LaunchArguments.ExtractPlaceId(args.RobloxArguments);
        GameProfile? profile = safeMode ? null : settings.FindProfile(placeId);

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

        if (safeMode)
            window.Title += " (safe mode: no mods or flags)";
        else if (profile is not null)
            window.Title += $" ({(string.IsNullOrWhiteSpace(profile.Name) ? "game profile" : profile.Name)})";

        window.Show();

        RobloxBootstrapper? bootstrapper = null;
        string? executable = null;

        try
        {
            for (int attempt = 0; attempt < 2 && executable is null; attempt++)
            {
                bootstrapper?.Dispose();
                bootstrapper = new RobloxBootstrapper(launchSettings, AppState.Load(), new Deployment(launchSettings.Channel), window, profile);

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
                    launchSettings.Channel = Deployment.DefaultChannel;
                    settings.Save();
                }
                finally
                {
                    window.CancelRequested -= cancel;
                }
            }

            if (executable is null || bootstrapper is null)
                return;

            DateTime launchedUtc = DateTime.UtcNow;
            Process roblox = bootstrapper.StartRoblox(LaunchArguments.RefreshLaunchTime(args.RobloxArguments));

            var integrations = new IntegrationRunner();
            integrations.StartAll(settings.CustomIntegrations);

            // Give Roblox a moment to show its window before ours disappears.
            await Task.Delay(1500);
            window.Finish();

            await Task.Run(() => Cleaner.Run(settings));

            bool watch = settings.TrackPlaytime || settings.BreakReminderMinutes > 0 || integrations.HasAutoClose;
            if (watch)
                await WatchSessionAsync(roblox, launchedUtc, settings);

            integrations.CloseAutoClose();
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

    /// <summary>
    /// Stays in the background while Roblox runs: follows its log to record time per game and
    /// shows a break reminder if one is set. Returns when Roblox closes.
    /// </summary>
    private static async Task WatchSessionAsync(Process roblox, DateTime launchedUtc, AppSettings settings)
    {
        long currentPlace = 0;
        DateTime? joinedAt = null;
        DateTime? reminderBase = null;   // when the current stretch of play started
        TimeSpan nextReminder = TimeSpan.FromMinutes(Math.Max(settings.BreakReminderMinutes, 1));
        BreakReminderWindow? reminder = null;
        var names = new Dictionary<long, string>();

        void EndSession(DateTime now)
        {
            if (joinedAt is { } start && currentPlace != 0 && settings.TrackPlaytime)
            {
                try { PlayHistory.Append(new PlaySession { PlaceId = currentPlace, StartUtc = start, EndUtc = now }); }
                catch (Exception ex) { Logger.WriteException("LaunchFlow::History", ex); }
            }

            joinedAt = null;
            currentPlace = 0;
        }

        async void LookUpName(long place)
        {
            if (names.ContainsKey(place))
                return;

            names[place] = $"Place {place}";
            string? name = await RobloxGames.GetNameAsync(place);

            if (name is not null)
            {
                names[place] = name;
                try { PlayHistory.SetName(place, name); } catch { /* cache only */ }
            }
        }

        void OnEvent(LogEvent e)
        {
            DateTime now = DateTime.UtcNow;

            switch (e.Kind)
            {
                case LogEventKind.Joined:
                    EndSession(now);
                    currentPlace = e.PlaceId;
                    joinedAt = now;
                    reminderBase ??= now;
                    LookUpName(e.PlaceId);
                    Logger.WriteLine("LaunchFlow", $"Playing place {e.PlaceId}");
                    break;

                case LogEventKind.Left:
                    EndSession(now);
                    break;
            }
        }

        // Break reminders are checked on a timer so they fire even if the log goes quiet.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) =>
        {
            if (settings.BreakReminderMinutes <= 0 || reminderBase is null || currentPlace == 0 || reminder is not null)
                return;

            TimeSpan played = DateTime.UtcNow - reminderBase.Value;
            if (played < nextReminder)
                return;

            string game = names.TryGetValue(currentPlace, out string? n) ? n : "Roblox";
            reminder = new BreakReminderWindow(game, played);
            reminder.Closed += (_, _) =>
            {
                // "Remind me later" = 15 more minutes; otherwise a full interval from now.
                nextReminder = played + (reminder!.Snoozed ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(settings.BreakReminderMinutes));
                reminder = null;
            };
            reminder.Show();
        };
        timer.Start();

        using var cts = new CancellationTokenSource();
        Task watcher = new RobloxLogWatcher().WatchAsync(Paths.RobloxLogs, launchedUtc, OnEvent, cts.Token);

        try
        {
            await RobloxProcess.WaitForExitAsync(roblox, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.WriteException("LaunchFlow::Wait", ex);
        }
        finally
        {
            timer.Stop();
            reminder?.Close();
            cts.Cancel();

            try { await watcher; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.WriteException("LaunchFlow::LogWatcher", ex); }

            EndSession(DateTime.UtcNow);
            Logger.WriteLine("LaunchFlow", "Roblox closed");
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
