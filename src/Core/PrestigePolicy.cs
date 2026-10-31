using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Firebot.Core;

internal enum PrestigeRateState
{
    Invalid,
    Baseline,
    TooSoon,
    Growing,
    Slow,
    Trigger
}

internal enum PrestigeTrigger
{
    None,
    Efficiency,
    BossRetries,
    StallTimer
}

internal readonly struct PrestigeRateResult
{
    public PrestigeRateResult(PrestigeRateState state, int streak, bool saveSample, double rate = 0, double limit = 0)
    {
        State = state;
        Streak = streak;
        SaveSample = saveSample;
        Rate = rate;
        Limit = limit;
    }

    public PrestigeRateState State { get; }
    public int Streak { get; }
    public bool SaveSample { get; }
    public double Rate { get; }
    public double Limit { get; }
}

/// <summary>Conservative parsing and rate comparisons for the temple's free prestige preview.</summary>
internal static class PrestigePolicy
{
    private static readonly Regex GainPattern = new(
        @"^\+\s*(?<number>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)\s*(?<suffix>[KMB]?)\s*%$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RunTimePattern = new(
        @"^(?:(?<days>\d+)\s*d\s*)?(?<hours>\d+):(?<minutes>[0-5]\d):(?<seconds>[0-5]\d)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ShortRunTimePattern = new(
        @"^(?<minutes>\d+):(?<seconds>[0-5]\d)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex RunTimeUnitsPattern = new(
        @"^(?<days>\d+)\s*d\s+(?<hours>\d{1,2})\s*h\s+(?<minutes>[0-5]\d)\s*m\s+(?<seconds>[0-5]\d)\s*s$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static PrestigeTrigger SelectTrigger(bool efficiencyTriggered, bool bossRetriesReached, bool stallTimerReached)
    {
        if (efficiencyTriggered) return PrestigeTrigger.Efficiency;
        if (bossRetriesReached) return PrestigeTrigger.BossRetries;
        return stallTimerReached ? PrestigeTrigger.StallTimer : PrestigeTrigger.None;
    }

    public static bool TryParseGain(string text, out double gain)
    {
        gain = 0;
        var match = GainPattern.Match((text ?? string.Empty).Trim());
        if (!match.Success ||
            !double.TryParse(match.Groups["number"].Value.Replace(",", ""), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            return false;

        var multiplier = match.Groups["suffix"].Value.ToUpperInvariant() switch
        {
            "K" => 1e3,
            "M" => 1e6,
            "B" => 1e9,
            _ => 1
        };
        gain = value * multiplier;
        return double.IsFinite(gain) && gain > 0;
    }

    public static bool TryParseRunHours(string text, out double hours)
    {
        hours = 0;
        var value = (text ?? string.Empty).Trim();
        var match = RunTimePattern.Match(value);
        if (!match.Success) match = RunTimeUnitsPattern.Match(value);
        if (!match.Success)
        {
            var shortMatch = ShortRunTimePattern.Match(value);
            if (shortMatch.Success &&
                double.TryParse(shortMatch.Groups["minutes"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var shortMinutes) &&
                int.TryParse(shortMatch.Groups["seconds"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var shortSeconds))
            {
                hours = shortMinutes / 60d + shortSeconds / 3600d;
                return double.IsFinite(hours);
            }
        }
        if (!match.Success ||
            !double.TryParse(match.Groups["hours"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(match.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var s))
            return false;

        var days = 0d;
        if (match.Groups["days"].Success &&
            !double.TryParse(match.Groups["days"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out days))
            return false;
        if (match.Groups["days"].Success && h >= 24) return false;
        hours = days * 24 + h + m / 60d + s / 3600d;
        return double.IsFinite(hours);
    }

    public static bool IsNewRun(double previousHours, double currentHours)
        => previousHours >= 0 && currentHours >= 0 && currentHours + 1d / 60d < previousHours;

    public static bool RunAgeDiscontinuous(double previousHours, double currentHours, DateTime previousAt, DateTime now)
    {
        if (!double.IsFinite(previousHours) || !double.IsFinite(currentHours) ||
            previousHours < 0 || currentHours < 0 || previousAt == default || previousAt > now)
            return true;
        if (IsNewRun(previousHours, currentHours)) return true;
        return currentHours - previousHours > (now - previousAt).TotalHours + 2d / 60d;
    }

    public static PrestigeRateResult EvaluateRate(double gain, double playedHours, double previousGain,
        DateTime previousAt, DateTime now, int previousStreak, int efficiencyPercent)
    {
        if (!double.IsFinite(gain) || gain <= 0 || !double.IsFinite(playedHours) || playedHours <= 0 ||
            efficiencyPercent <= 0)
            return new PrestigeRateResult(PrestigeRateState.Invalid, 0, false);

        var elapsed = now - previousAt;
        if (previousAt == default || previousAt > now || elapsed > TimeSpan.FromHours(2) ||
            !double.IsFinite(previousGain) || previousGain < 0 || gain < previousGain)
            return new PrestigeRateResult(PrestigeRateState.Baseline, 0, true);

        if (elapsed < TimeSpan.FromHours(1))
            return new PrestigeRateResult(PrestigeRateState.TooSoon, previousStreak, false);

        var rate = (gain - previousGain) / elapsed.TotalHours;
        var limit = gain / playedHours * efficiencyPercent / 100d;
        if (!double.IsFinite(rate) || !double.IsFinite(limit))
            return new PrestigeRateResult(PrestigeRateState.Invalid, 0, false);

        var streak = rate < limit ? previousStreak + 1 : 0;
        var state = streak >= 2 ? PrestigeRateState.Trigger
            : streak == 1 ? PrestigeRateState.Slow
            : PrestigeRateState.Growing;
        return new PrestigeRateResult(state, streak, true, rate, limit);
    }
}
