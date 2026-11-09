using System.Diagnostics;

namespace RobloxModManager.Core;

public static class Logger
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    /// <summary>Raised for every line written. Handlers can run on any thread.</summary>
    public static event Action<string>? LineWritten;

    public static void Initialize(string logsDirectory)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
            FilePath = Path.Combine(logsDirectory, $"{AppInfo.ProjectName}_{DateTime.Now:yyyyMMdd'T'HHmmss}_{Environment.ProcessId}.log");
            _writer = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        }
        catch
        {
            _writer = null; // logging must never stop the app from working
        }
    }

    public static void WriteLine(string ident, string message)
    {
        string line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} [{ident}] {message}";

        lock (Sync)
        {
            try { _writer?.WriteLine(line); } catch { /* ignore */ }
        }

        Debug.WriteLine(line);

        try { LineWritten?.Invoke(line); } catch { /* ignore subscriber errors */ }
    }

    public static void WriteException(string ident, Exception ex) => WriteLine(ident, ex.ToString());
}
