using System.Net;
using System.Text.Json;

namespace RobloxModManager.Core;

public class ChannelNotFoundException : Exception
{
    public ChannelNotFoundException(string channel, HttpStatusCode? statusCode)
        : base($"The Roblox channel \"{channel}\" does not exist or is not available to your account ({(int?)statusCode}).") { }
}

public class ChecksumFailedException : Exception
{
    public ChecksumFailedException(string message) : base(message) { }
}

/// <summary>Finds the fastest Roblox CDN mirror and asks Roblox which version is current for a channel.</summary>
public class Deployment
{
    public const string DefaultChannel = "production";
    public const string BinaryType = "WindowsPlayer";

    /// <summary>Mirror URL and how many seconds to wait before testing it (so the primary wins ties).</summary>
    public static readonly IReadOnlyDictionary<string, int> DefaultCdnUrls = new Dictionary<string, int>
    {
        ["https://setup.rbxcdn.com"] = 0,
        ["https://setup-aws.rbxcdn.com"] = 2,
        ["https://setup-ak.rbxcdn.com"] = 2,
        ["https://s3.amazonaws.com/setup.roblox.com"] = 4
    };

    public static readonly IReadOnlyList<string> DefaultApiHosts = new[]
    {
        "https://clientsettingscdn.roblox.com",
        "https://clientsettings.roblox.com"
    };

    private static readonly HashSet<HttpStatusCode> BadChannelCodes = new()
    {
        HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound
    };

    private readonly IReadOnlyDictionary<string, int> _cdnUrls;
    private readonly IReadOnlyList<string> _apiHosts;

    public string Channel { get; }
    public string CdnUrl { get; private set; } = "";

    public Deployment(string channel, IReadOnlyDictionary<string, int>? cdnUrls = null, IReadOnlyList<string>? apiHosts = null)
    {
        Channel = string.IsNullOrWhiteSpace(channel) ? DefaultChannel : channel.Trim();
        _cdnUrls = cdnUrls ?? DefaultCdnUrls;
        _apiHosts = apiHosts ?? DefaultApiHosts;
    }

    public static bool IsDefault(string? channel) =>
        string.IsNullOrWhiteSpace(channel)
        || channel.Equals(DefaultChannel, StringComparison.OrdinalIgnoreCase)
        || channel.Equals("live", StringComparison.OrdinalIgnoreCase);

    public bool IsDefaultChannel => IsDefault(Channel);

    private static async Task<string> TestConnectionAsync(string url, int delaySeconds, CancellationToken ct)
    {
        if (delaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);

        using var response = await Http.Client.GetAsync($"{url}/versionStudio", ct);
        response.EnsureSuccessStatusCode();

        // A captive portal or proxy would answer with HTML; real mirrors return a version hash.
        string body = (await response.Content.ReadAsStringAsync(ct)).Trim();
        if (!body.StartsWith("version-", StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected response from {url}.");

        return url;
    }

    /// <summary>Tests every mirror in parallel; the first healthy one becomes <see cref="CdnUrl"/>.</summary>
    public async Task InitializeConnectivityAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var tasks = _cdnUrls.Select(entry => TestConnectionAsync(entry.Key, entry.Value, cts.Token)).ToList();
        var errors = new List<Exception>();
        string? winner = null;

        while (tasks.Count > 0 && winner is null)
        {
            Task<string> finished = await Task.WhenAny(tasks);
            tasks.Remove(finished);

            if (finished.IsFaulted)
            {
                errors.Add(finished.Exception!.GetBaseException());
                Logger.WriteLine("Deployment::Connectivity", $"Mirror failed: {errors[^1].Message}");
            }
            else if (finished.IsCompletedSuccessfully)
            {
                winner = finished.Result;
            }
        }

        cts.Cancel(); // stop the slower mirrors

        foreach (var leftover in tasks)
            _ = leftover.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observe

        ct.ThrowIfCancellationRequested();

        if (winner is null)
        {
            throw errors.Count > 0
                ? new HttpRequestException("Unable to reach any Roblox download server.", errors[0])
                : new TimeoutException("All connection attempts to Roblox timed out.");
        }

        CdnUrl = winner;
        Logger.WriteLine("Deployment::Connectivity", $"Using {CdnUrl}");
    }

    /// <summary>Builds a CDN URL for a resource such as "/version-xxx-rbxPkgManifest.txt".</summary>
    public string GetLocation(string resource)
    {
        if (string.IsNullOrEmpty(CdnUrl))
            throw new InvalidOperationException("Connectivity has not been initialised.");

        // Roblox serves every non-production channel from the shared "common" folder.
        return CdnUrl + (IsDefaultChannel ? "" : "/channel/common") + resource;
    }

    public async Task<ClientVersion> GetInfoAsync(CancellationToken ct = default)
    {
        string path = $"v2/client-version/{BinaryType}";
        if (!IsDefaultChannel)
            path += $"/channel/{Uri.EscapeDataString(Channel.ToLowerInvariant())}";

        Exception? last = null;

        foreach (string host in _apiHosts)
        {
            try
            {
                using var response = await Http.Client.GetAsync($"{host}/{path}", ct);

                if (!IsDefaultChannel && BadChannelCodes.Contains(response.StatusCode))
                    throw new ChannelNotFoundException(Channel, response.StatusCode);

                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(ct);
                var info = JsonSerializer.Deserialize<ClientVersion>(json, JsonFile.Options);

                if (info is null || string.IsNullOrEmpty(info.VersionGuid))
                    throw new InvalidDataException("Roblox returned an unexpected version response.");

                return info;
            }
            catch (ChannelNotFoundException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                Logger.WriteLine("Deployment::GetInfo", $"{host} failed: {ex.Message}");
            }
        }

        throw new HttpRequestException("Unable to get Roblox version information.", last);
    }
}
