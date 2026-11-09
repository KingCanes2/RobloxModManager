using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RobloxModManager.Core;
using RobloxModManager.UI;
using RobloxModManager.UI.Dialogs;
using RobloxModManager.UI.Installer;
using RobloxModManager.UI.LaunchMenu;
using RobloxModManager.UI.Settings;
using RobloxModManager.UI.Uninstaller;

namespace RobloxModManager;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.WriteLine("App", $"Fatal: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.WriteException("App::UnobservedTask", args.Exception);
            args.SetObserved();
        };

        var args = LaunchArguments.Parse(e.Args);

        bool installed = InstallService.IsInstalled(out string? installDirectory);
        bool runningFromInstall = InstallService.IsRunningFromInstall(out _);

        if (installed && installDirectory is not null)
        {
            Paths.Initialize(installDirectory);
            Logger.Initialize(Paths.Logs);
        }
        else
        {
            // Not installed yet: keep logs out of the folder the user is about to pick.
            Logger.Initialize(Path.Combine(Path.GetTempPath(), AppInfo.ProjectName, "Logs"));
        }

        Logger.WriteLine("App", $"{AppInfo.DisplayName} {AppInfo.Version} starting (mode {args.Mode}, installed: {installed}, from install: {runningFromInstall})");

        ThemeManager.Apply(installed ? AppSettings.Load().Theme : AppTheme.System);

        try
        {
            if (args.Mode == LaunchMode.Uninstall)
            {
                if (installed)
                    new UninstallerWindow().ShowDialog();
                else
                    MessageDialog.Info("Not installed", $"{AppInfo.DisplayName} isn't installed on this PC.");
            }
            else if (!installed || (!runningFromInstall && args.Mode == LaunchMode.Menu))
            {
                // First run, or a downloaded copy opened by hand: install (or update the existing install).
                await RunInstallerAsync(installDirectory, installed);
            }
            else
            {
                await RunModeAsync(args);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteException("App", ex);
            MessageDialog.Error("Something went wrong", ex.Message, ex);
        }
        finally
        {
            Shutdown();
        }
    }

    private static async Task RunInstallerAsync(string? existingDirectory, bool isUpdate)
    {
        var installer = new InstallerWindow(existingDirectory ?? Paths.DefaultBase, isUpdate);
        installer.ShowDialog();

        if (!installer.Completed)
            return;

        // Install() pointed Paths at the chosen folder, so the rest of the app works from here.
        Logger.Initialize(Paths.Logs);

        switch (installer.NextAction)
        {
            case InstallerNextAction.LaunchRoblox:
                await LaunchFlow.RunPlayerAsync(LaunchArguments.Parse(new[] { "-player" }));
                break;

            case InstallerNextAction.OpenSettings:
                await OpenSettingsAsync(null);
                break;
        }
    }

    private static async Task RunModeAsync(LaunchArguments args)
    {
        switch (args.Mode)
        {
            case LaunchMode.Player:
                await LaunchFlow.RunPlayerAsync(args);
                break;

            case LaunchMode.Settings:
                await OpenSettingsAsync(null);
                break;

            default:
                var menu = new LaunchMenuWindow();
                menu.ShowDialog();

                switch (menu.Choice)
                {
                    case LaunchMenuChoice.LaunchRoblox:
                        await LaunchFlow.RunPlayerAsync(LaunchArguments.Parse(new[] { "-player" }));
                        break;
                    case LaunchMenuChoice.Settings:
                        await OpenSettingsAsync(null);
                        break;
                    case LaunchMenuChoice.About:
                        await OpenSettingsAsync(SettingsPages.About);
                        break;
                }
                break;
        }
    }

    private static async Task OpenSettingsAsync(string? page)
    {
        bool launchAfter;

        using (var mutex = new Mutex(true, $"{AppInfo.ProjectName}-Settings", out bool createdNew))
        {
            if (!createdNew)
            {
                MessageDialog.Info("Settings are already open", "Switch to the settings window that's already open.");
                return;
            }

            try
            {
                var window = new SettingsWindow(page);
                window.ShowDialog();
                launchAfter = window.LaunchAfterClose;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        if (launchAfter)
            await LaunchFlow.RunPlayerAsync(LaunchArguments.Parse(new[] { "-player" }));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Logger.WriteException("App::Unhandled", e.Exception);

        try
        {
            MessageDialog.Error("Something went wrong", $"{AppInfo.DisplayName} ran into a problem and has to close.\n\n{e.Exception.Message}", e.Exception);
        }
        catch
        {
            // the dialog itself failed; nothing more we can show
        }

        Shutdown(1);
    }
}
