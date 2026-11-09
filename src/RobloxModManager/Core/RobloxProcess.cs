using System.Diagnostics;

namespace RobloxModManager.Core;

public static class RobloxProcess
{
    public const string PlayerProcessName = "RobloxPlayerBeta";

    public static bool IsRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(PlayerProcessName);
            bool running = processes.Length > 0;

            foreach (var process in processes)
                process.Dispose();

            return running;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Waits until Roblox is gone. Roblox can hand a launch over to another process, so after the launched
    /// process exits we keep waiting while any player process is still running.
    /// </summary>
    public static async Task WaitForExitAsync(Process launched, CancellationToken ct)
    {
        try { await launched.WaitForExitAsync(ct); }
        catch (InvalidOperationException) { /* process object was not started by us */ }

        int idleChecks = 0;

        while (idleChecks < 3)
        {
            idleChecks = IsRunning() ? 0 : idleChecks + 1;
            await Task.Delay(1000, ct);
        }
    }
}
