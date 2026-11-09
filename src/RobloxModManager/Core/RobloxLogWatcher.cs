using System.Text.RegularExpressions;

namespace RobloxModManager.Core;

public enum LogEventKind { Joining, Joined, Left }

public sealed record LogEvent(LogEventKind Kind, long PlaceId, string JobId);

/// <summary>
/// Follows the log file Roblox writes while it runs, to see which game is being played.
/// Roblox's log lines look like "2026-01-01T10:00:00.000Z,12.3,ab12,6 [FLog::Output] ! Joining game '...' place 123 at 1.2.3.4".
/// (Line formats match the ones Fishstrap and Bloxstrap rely on.)
/// </summary>
public sealed class RobloxLogWatcher
{
    private const string JoiningEntry = "[FLog::Output] ! Joining game";
    private const string JoinedEntry = "[FLog::Network] serverId:";
    private const string DisconnectedEntry = "[FLog::Network] Time to disconnect replication data:";
    private const string LeavingEntry = "[FLog::SingleSurfaceApp] leaveUGCGameInternal";

    private static readonly Regex JoiningPattern =
        new(@"! Joining game '([0-9a-f\-]{36})' place ([0-9]+) at", RegexOptions.CultureInvariant);

    private long _pendingPlace;
    private string _pendingJob = "";
    private bool _inGame;

    /// <summary>Turns one log line into an event, or null if the line doesn't matter.</summary>
    public LogEvent? ParseLine(string line)
    {
        int space = line.IndexOf(' ');
        if (space < 0)
            return null;

        string message = line[(space + 1)..];

        if (message.StartsWith(JoiningEntry, StringComparison.Ordinal))
        {
            var match = JoiningPattern.Match(message);
            if (!match.Success || !long.TryParse(match.Groups[2].Value, out long placeId))
                return null;

            // A teleport goes straight from one game into the next: close the first one.
            LogEvent? leftPrevious = _inGame ? new LogEvent(LogEventKind.Left, _pendingPlace, _pendingJob) : null;

            _pendingPlace = placeId;
            _pendingJob = match.Groups[1].Value;
            _inGame = false;

            return leftPrevious ?? new LogEvent(LogEventKind.Joining, placeId, _pendingJob);
        }

        if (message.StartsWith(JoinedEntry, StringComparison.Ordinal) && _pendingPlace != 0 && !_inGame)
        {
            _inGame = true;
            return new LogEvent(LogEventKind.Joined, _pendingPlace, _pendingJob);
        }

        if ((message.StartsWith(DisconnectedEntry, StringComparison.Ordinal) || message.StartsWith(LeavingEntry, StringComparison.Ordinal)) && _inGame)
        {
            _inGame = false;
            var left = new LogEvent(LogEventKind.Left, _pendingPlace, _pendingJob);
            _pendingPlace = 0;
            _pendingJob = "";
            return left;
        }

        return null;
    }

    /// <summary>
    /// Feeds lines through <see cref="ParseLine"/>. A teleport line produces a Left for the old game, and the
    /// following Joined belongs to the new one, so callers only need Joined/Left.
    /// </summary>
    public IEnumerable<LogEvent> ParseLines(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            if (ParseLine(line) is { } e)
                yield return e;
        }
    }

    /// <summary>The newest Player log created at or after <paramref name="sinceUtc"/> (a few seconds of slack).</summary>
    public static string? FindLog(string logsDirectory, DateTime sinceUtc)
    {
        if (!Directory.Exists(logsDirectory))
            return null;

        return new DirectoryInfo(logsDirectory)
            .GetFiles("*.log")
            .Where(f => f.Name.Contains("Player", StringComparison.OrdinalIgnoreCase)
                        && !f.Name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase)
                        && f.CreationTimeUtc >= sinceUtc.AddSeconds(-10))
            .OrderByDescending(f => f.CreationTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// Waits for Roblox's log to appear, then follows it, calling <paramref name="onEvent"/> for every event,
    /// until <paramref name="ct"/> is cancelled. Returns quietly if no log shows up within a minute.
    /// </summary>
    public async Task WatchAsync(string logsDirectory, DateTime launchedUtc, Action<LogEvent> onEvent, CancellationToken ct)
    {
        string? path = null;

        for (int i = 0; i < 60 && path is null; i++)
        {
            path = FindLog(logsDirectory, launchedUtc);
            if (path is null)
                await Task.Delay(1000, ct);
        }

        if (path is null)
        {
            Logger.WriteLine("LogWatcher", "No Roblox log appeared; play time won't be recorded for this session");
            return;
        }

        Logger.WriteLine("LogWatcher", $"Following {Path.GetFileName(path)}");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);

            if (line is null)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            if (ParseLine(line) is { } e)
                onEvent(e);
        }
    }
}
