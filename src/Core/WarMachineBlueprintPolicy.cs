using System;
using System.Collections.Generic;

namespace Firebot.Core;

internal static class WarMachineBlueprintPolicy
{
    public static bool TryGetSlots(string mode, string priority, out int[] slots)
    {
        slots = Array.Empty<int>();
        switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "none": return true;
            case "damage": slots = new[] { 0 }; return true;
            case "health": slots = new[] { 1 }; return true;
            case "armor": slots = new[] { 2 }; return true;
            case "all": break;
            default: return false;
        }

        var result = new List<int>();
        foreach (var value in (priority ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var slot = value.Trim().ToLowerInvariant() switch
            {
                "damage" => 0,
                "health" => 1,
                "armor" => 2,
                _ => -1
            };
            if (slot < 0) return false;
            if (!result.Contains(slot)) result.Add(slot);
        }
        slots = result.ToArray();
        return true;
    }

    public static PriorityTierAction Combine(PriorityTierAction first, PriorityTierAction second)
        => first == PriorityTierAction.RetrySoon || second == PriorityTierAction.RetrySoon
            ? PriorityTierAction.RetrySoon
            : first == PriorityTierAction.Wait || second == PriorityTierAction.Wait
                ? PriorityTierAction.Wait : PriorityTierAction.Advance;
}
