namespace RobloxModManager.Core;

/// <summary>Deletes old logs and cache files so they do not pile up.</summary>
public static class Cleaner
{
    public static TimeSpan? ToTimeSpan(CleanerAge age) => age switch
    {
        CleanerAge.OneDay => TimeSpan.FromDays(1),
        CleanerAge.OneWeek => TimeSpan.FromDays(7),
        CleanerAge.TwoWeeks => TimeSpan.FromDays(14),
        CleanerAge.OneMonth => TimeSpan.FromDays(30),
        CleanerAge.TwoMonths => TimeSpan.FromDays(60),
        _ => null
    };

    public static void Run(AppSettings settings)
    {
        if (ToTimeSpan(settings.CleanerAge) is not { } maxAge)
            return;

        DateTime cutoff = DateTime.UtcNow - maxAge;

        try
        {
            if (settings.CleanAppLogs) DeleteOlderThan(Paths.Logs, cutoff);
            if (settings.CleanRobloxLogs) DeleteOlderThan(Paths.RobloxLogs, cutoff);
            if (settings.CleanRobloxCache) DeleteOlderThan(Paths.RobloxCache, cutoff);
        }
        catch (Exception ex)
        {
            Logger.WriteException("Cleaner::Run", ex);
        }
    }

    private static void DeleteOlderThan(string directory, DateTime cutoffUtc)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                    File.Delete(file);
            }
            catch
            {
                // in use or protected: leave it for next time
            }
        }
    }
}
