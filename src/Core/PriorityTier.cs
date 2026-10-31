using System;
using System.Collections.Generic;

namespace Firebot.Core;

internal enum PriorityTierAction
{
    Advance,
    Wait,
    RetrySoon
}

internal static class PriorityTier
{
    public static int Rank(IReadOnlyList<string> order, string name)
    {
        var target = (name ?? string.Empty).Trim();
        for (var i = 0; i < order.Count; i++)
            if (string.Equals(order[i]?.Trim(), target, StringComparison.OrdinalIgnoreCase))
                return i;
        return order.Count;
    }

    public static PriorityTierAction AfterPass(bool complete, bool available, bool hitLimit,
        bool clickable, double cost, double budget)
    {
        if (complete || !available) return PriorityTierAction.Advance;
        if (hitLimit && clickable && cost > 0 && budget >= cost) return PriorityTierAction.RetrySoon;
        return PriorityTierAction.Wait;
    }
}
