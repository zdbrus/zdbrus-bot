using System;
using System.Collections.Generic;

namespace Firebot.Core;

internal enum ExoticUpgradeAction
{
    Buy,
    Advance,
    Wait,
    RetrySoon
}

internal static class ExoticUpgradePolicy
{
    public static int Rank(IReadOnlyList<string> order, string name)
    {
        var target = (name ?? string.Empty).Trim();
        if (target.StartsWith("Skip stage ", StringComparison.OrdinalIgnoreCase)) target = "Skip stage";
        if (target.StartsWith("Skip wave ", StringComparison.OrdinalIgnoreCase)) target = "Skip wave";
        return PriorityTier.Rank(order, target);
    }

    public static ExoticUpgradeAction Decide(bool complete, bool levelLocked, bool hasButton,
        bool clickable, bool hitLimit, double cost, double balance)
    {
        if (complete || levelLocked) return ExoticUpgradeAction.Advance;
        if (!hasButton || !double.IsFinite(cost) || cost <= 0 ||
            !double.IsFinite(balance) || balance < 0 || balance < cost || !clickable)
            return ExoticUpgradeAction.Wait;
        return hitLimit ? ExoticUpgradeAction.RetrySoon : ExoticUpgradeAction.Buy;
    }
}
