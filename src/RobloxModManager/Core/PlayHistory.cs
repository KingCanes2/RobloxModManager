using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobloxModManager.Core;

public sealed class PlaySession
{
    public long PlaceId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => EndUtc > StartUtc ? EndUtc - StartUtc : TimeSpan.Zero;
}

public sealed record GameStats(long PlaceId, string Name, TimeSpan Total, int Sessions, DateTime LastPlayedUtc);

/// <summary>Time played per game (PlayHistory.json). Game names are cached so the page works offline.</summary>
public sealed class PlayHistory
{
    /// <summary>Very short visits (failed joins, instant teleports) are not worth recording.</summary>
    public static readonly TimeSpan MinimumSession = TimeSpan.FromSeconds(30);

    public List<PlaySession> Sessions { get; set; } = new();
    public Dictionary<long, string> GameNames { get; set; } = new();

    public static string FilePath => Path.Combine(Paths.Base, "PlayHistory.json");

    private static readonly object FileLock = new();

    public static PlayHistory Load() => JsonFile.Load<PlayHistory>(FilePath);

    public void Save() => JsonFile.Save(FilePath, this);

    /// <summary>Adds one session to the file on disk (re-reads it first, so two launches can't overwrite each other).</summary>
    public static void Append(PlaySession session)
    {
        if (session.Duration < MinimumSession)
            return;

        lock (FileLock)
        {
            var history = Load();
            history.Sessions.Add(session);
            history.Save();
        }
    }

    /// <summary>Caches a game's name on disk (re-reads first, like <see cref="Append"/>).</summary>
    public static void SetName(long placeId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (FileLock)
        {
            var history = Load();
            history.GameNames[placeId] = name;
            history.Save();
        }
    }

    public static void Clear()
    {
        lock (FileLock)
            new PlayHistory().Save();
    }

    public string NameFor(long placeId) =>
        GameNames.TryGetValue(placeId, out string? name) && !string.IsNullOrWhiteSpace(name) ? name : $"Place {placeId}";

    public TimeSpan TotalSince(DateTime sinceUtc) =>
        Sessions.Where(s => s.EndUtc >= sinceUtc)
            .Aggregate(TimeSpan.Zero, (sum, s) => sum + (s.StartUtc >= sinceUtc ? s.Duration : s.EndUtc - sinceUtc));

    public List<GameStats> PerGame() =>
        Sessions.GroupBy(s => s.PlaceId)
            .Select(g => new GameStats(
                g.Key,
                NameFor(g.Key),
                g.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration),
                g.Count(),
                g.Max(s => s.EndUtc)))
            .OrderByDescending(s => s.Total)
            .ToList();

    /// <summary>Consecutive days (ending today or yesterday, local time) with at least one session.</summary>
    public int DayStreak(DateTime nowLocal)
    {
        var days = Sessions.Select(s => s.StartUtc.ToLocalTime().Date).ToHashSet();
        DateTime day = nowLocal.Date;

        if (!days.Contains(day))
            day = day.AddDays(-1);

        int streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }

    public static string Format(TimeSpan time)
    {
        if (time.TotalMinutes < 1)
            return "under a minute";
        if (time.TotalHours < 1)
            return $"{(int)time.TotalMinutes} min";
        return time.Minutes == 0 ? $"{(int)time.TotalHours} h" : $"{(int)time.TotalHours} h {time.Minutes} min";
    }
}

/// <summary>Looks up a game's name from its place ID using Roblox's public web APIs (no sign-in needed).</summary>
public static class RobloxGames
{
    public static async Task<string?> GetNameAsync(long placeId, CancellationToken ct = default,
        string apisHost = "https://apis.roblox.com", string gamesHost = "https://games.roblox.com")
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            string universeJson = await Http.Client.GetStringAsync($"{apisHost}/universes/v1/places/{placeId}/universe", cts.Token);
            using var universe = JsonDocument.Parse(universeJson);
            if (!universe.RootElement.TryGetProperty("universeId", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
                return null;

            long universeId = idElement.GetInt64();
            string gamesJson = await Http.Client.GetStringAsync($"{gamesHost}/v1/games?universeIds={universeId}", cts.Token);
            using var games = JsonDocument.Parse(gamesJson);

            if (games.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0
                && data[0].TryGetProperty("name", out var name))
                return name.GetString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Logger.WriteLine("RobloxGames", $"Couldn't look up place {placeId}: {ex.Message}");
        }

        return null;
    }

    /// <summary>Accepts a place ID or a Roblox game link (roblox.com/games/123/...).</summary>
    public static long? ParsePlaceInput(string input)
    {
        input = input.Trim();

        if (long.TryParse(input, out long direct) && direct > 0)
            return direct;

        var match = System.Text.RegularExpressions.Regex.Match(input, @"roblox\.com/(?:[a-z]{2}/)?games/(\d{1,19})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && long.TryParse(match.Groups[1].Value, out long fromLink))
            return fromLink;

        return LaunchArguments.ExtractPlaceId(input);
    }
}
