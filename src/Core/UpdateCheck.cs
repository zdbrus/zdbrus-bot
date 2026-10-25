using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Firebot.Core;

/// <summary>
///     Asks GitHub once per game start whether a newer Zdbrus Bot release exists. Never downloads or installs anything:
///     it only tells the player (log line and the F1 panel title) where to get the update.
/// </summary>
internal static class UpdateCheck
{
    public const string ReleasesUrl = "https://github.com/zdbrus/zdbrus-bot/releases/latest";
    private const string Api = "https://api.github.com/repos/zdbrus/zdbrus-bot/releases/latest";

    /// <summary>The newer version, or null when up to date or not known yet.</summary>
    public static string Newer { get; private set; }

    public static void Start()
    {
        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ZdbrusBot/" + Brand.Version);
                var json = await http.GetStringAsync(Api);
                var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][^\"]*)\"");
                if (!m.Success) return;
                var latest = m.Groups[1].Value;
                if (IsNewer(latest, Brand.Version))
                {
                    Newer = latest;
                    Logger.Warning($"Zdbrus Bot {latest} is available (you have {Brand.Version}). Download: {ReleasesUrl}");
                }
                else Logger.Info($"Zdbrus Bot is up to date ({Brand.Version}).");
            }
            catch (Exception e)
            {
                Logger.Debug($"[Update] Could not check for updates: {e.Message}");
            }
        });
    }

    private static bool IsNewer(string a, string b)
    {
        int[] P(string s)
        {
            var parts = Regex.Matches(s, @"\d+");
            var r = new int[3];
            for (var i = 0; i < 3 && i < parts.Count; i++) r[i] = int.Parse(parts[i].Value);
            return r;
        }

        var x = P(a); var y = P(b);
        for (var i = 0; i < 3; i++)
            if (x[i] != y[i]) return x[i] > y[i];
        return false;
    }
}
