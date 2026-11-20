using System;

namespace Firebot.Core;

/// <summary>
///     Strange Dust the player wants left alone (guardian evolutions, the 1,000 dust for Tier 2 soulstones).
///     Used by the Alchemist and guardian Enlightenment, which both spend dust.
/// </summary>
internal static class StrangeDustPolicy
{
    /// <summary>Dust one Enlightenment click costs (the guide lists 20).</summary>
    public const double EnlightenmentCost = 20;

    /// <summary>True when spending cost still leaves at least keep behind. An unreadable balance never spends.</summary>
    public static bool CanSpend(double balance, double cost, double keep)
    {
        if (!double.IsFinite(balance) || !double.IsFinite(cost) || cost < 0) return false;
        if (!double.IsFinite(keep) || keep < 0) keep = 0;
        return balance - cost >= keep;
    }
}
