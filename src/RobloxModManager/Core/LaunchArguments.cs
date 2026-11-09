using System.Text.RegularExpressions;

namespace RobloxModManager.Core;

public enum LaunchMode { Menu, Player, Settings, Uninstall }

/// <summary>Parses the command line: -player, -settings, -uninstall or a roblox-player:/roblox: link.</summary>
public sealed class LaunchArguments
{
    /// <summary>Only plain protocol links are passed on to Roblox: no whitespace, quotes or control characters.</summary>
    private static readonly Regex SafeProtocolLink = new(@"^(roblox|roblox-player):[^\s""\x00-\x1f]{0,4096}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public LaunchMode Mode { get; private set; } = LaunchMode.Menu;

    /// <summary>What gets handed to RobloxPlayerBeta.exe. "--app" opens the Roblox home screen.</summary>
    public string RobloxArguments { get; private set; } = "--app";

    /// <summary>True when a protocol link was rejected because it contained unsafe characters.</summary>
    public bool RejectedLink { get; private set; }

    public static LaunchArguments Parse(IEnumerable<string> args)
    {
        var result = new LaunchArguments();

        foreach (string raw in args)
        {
            string arg = raw.Trim();

            if (arg.Equals("-uninstall", StringComparison.OrdinalIgnoreCase) || arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                result.Mode = LaunchMode.Uninstall;
            }
            else if (arg.Equals("-settings", StringComparison.OrdinalIgnoreCase) || arg.Equals("--settings", StringComparison.OrdinalIgnoreCase))
            {
                result.Mode = LaunchMode.Settings;
            }
            else if (arg.Equals("-menu", StringComparison.OrdinalIgnoreCase))
            {
                result.Mode = LaunchMode.Menu;
            }
            else if (arg.Equals("-player", StringComparison.OrdinalIgnoreCase) || arg.Equals("--player", StringComparison.OrdinalIgnoreCase))
            {
                result.Mode = LaunchMode.Player;
            }
            else if (arg.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
            {
                result.Mode = LaunchMode.Player;

                if (SafeProtocolLink.IsMatch(arg))
                    result.RobloxArguments = arg;
                else
                    result.RejectedLink = true;
            }
        }

        return result;
    }

    /// <summary>
    /// Roblox links carry a launch timestamp; refresh it so a link that sat in the browser for a while still works.
    /// </summary>
    /// <summary>
    /// Finds the game's place ID in a launch link. Website links carry it URL-encoded inside the
    /// PlaceLauncher URL (placeId%3D123), app links as roblox://placeId=123 or ...?placeId=123.
    /// </summary>
    public static long? ExtractPlaceId(string robloxArguments)
    {
        string text = robloxArguments;

        for (int i = 0; i < 3; i++)
        {
            var match = Regex.Match(text, @"placeId=(\d{1,19})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && long.TryParse(match.Groups[1].Value, out long placeId) && placeId > 0)
                return placeId;

            string decoded;
            try { decoded = Uri.UnescapeDataString(text.Replace('+', ' ')); }
            catch (UriFormatException) { break; }

            if (decoded == text)
                break;

            text = decoded;
        }

        return null;
    }

    public static string RefreshLaunchTime(string robloxArguments)
    {
        return Regex.Replace(robloxArguments, @"launchtime:\d+", $"launchtime:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", RegexOptions.IgnoreCase);
    }
}
