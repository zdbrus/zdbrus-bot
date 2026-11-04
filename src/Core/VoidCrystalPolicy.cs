using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebot.Core;

public sealed class VoidCrystalOption
{
    public int Hero;
    public string Name;
    public int Slot;
    public int Level;
    public double Cost;
    public string CurrentEffect;
    public string NextEffect;
}

public static class VoidCrystalPolicy
{
    public const double MaxInteger = 9007199254740991d;
    public const int MaxEnchants = 40;

    public static bool WholeAmount(double value) =>
        double.IsFinite(value) && value >= 0 && value <= MaxInteger && value == Math.Truncate(value);

    public static bool ValidGoal(string goal) => goal is "damage" or "health" or "armor" or "gold";

    public static bool Matches(VoidCrystalOption item, string goal, int target)
    {
        if (item == null || !ValidGoal(goal) || target < -1 || item.Hero < 0 ||
            item.Slot is < 0 or > 7 || item.Level < 0 || !WholeAmount(item.Cost) || item.Cost <= 0)
            return false;
        if (item.Slot < 3 && item.Hero != target) return false;
        return goal switch
        {
            "damage" => item.Slot is 0 or 3 or 7,
            "health" => item.Slot is 1 or 4 or 7,
            "armor" => item.Slot is 2 or 5 or 7,
            "gold" => item.Slot == 6,
            _ => false,
        };
    }

    // Each enchant doubles this slot's effect. Rank comparable effects by real price, not gear power.
    public static IEnumerable<VoidCrystalOption> Rank(IEnumerable<VoidCrystalOption> items, string goal, int target) =>
        items.Where(item => Matches(item, goal, target)).OrderBy(item => item.Cost)
            .ThenByDescending(item => item.Slot == 7).ThenByDescending(item => item.Slot >= 3)
            .ThenBy(item => item.Hero).ThenBy(item => item.Slot);

    public static bool CanSpend(VoidCrystalOption expected, VoidCrystalOption current,
        double budget, double spent, double balance) =>
        expected != null && current != null && WholeAmount(budget) && budget > 0 &&
        WholeAmount(spent) && spent <= budget && WholeAmount(balance) &&
        WholeAmount(current.Cost) && current.Cost > 0 &&
        expected.Hero == current.Hero && expected.Slot == current.Slot &&
        expected.Level == current.Level && expected.Cost == current.Cost &&
        current.Cost <= budget - spent && current.Cost <= balance;

    public static bool Confirmed(int beforeLevel, int afterLevel, double beforeBalance, double afterBalance, double cost) =>
        beforeLevel >= 0 && afterLevel == beforeLevel + 1 &&
        WholeAmount(beforeBalance) && WholeAmount(afterBalance) && WholeAmount(cost) && cost > 0 &&
        beforeBalance - afterBalance == cost;
}
