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
    public int Rarity;
    public double NativeEffect;
    public double EffectGain;
    public double MultiplierGain;
    public double PowerGain;
    public bool Active;
    public bool DamageReady;
    public bool HealthReady;
    public bool ArmorReady;
    public double DamageShare;
    public double HealthShare;
    public double ArmorShare;
}

public static class VoidCrystalPolicy
{
    public const double MaxInteger = 9007199254740991d;
    public const int MaxEnchants = 40;

    public static bool WholeAmount(double value) =>
        double.IsFinite(value) && value >= 0 && value <= MaxInteger && value == Math.Truncate(value);

    public static bool ValidGoal(string goal) => goal is "balanced" or "damage" or "health" or "armor" or "gold" or "power";

    public static bool Matches(VoidCrystalOption item, string goal, int target)
    {
        if (item == null || !ValidGoal(goal) || target < -1 || item.Hero < 0 ||
            item.Slot is < 0 or > 7 || item.Level < 0 || !WholeAmount(item.Cost) || item.Cost <= 0 ||
            !double.IsFinite(item.MultiplierGain) || item.MultiplierGain <= 0 ||
            !double.IsFinite(item.PowerGain) || item.PowerGain <= 0)
            return false;
        if (goal is "balanced" or "power") return target == -1 || item.Hero == target;
        if (target == -1 && (goal switch
            {
                "damage" => !item.DamageReady,
                "health" => !item.HealthReady,
                "armor" => !item.ArmorReady,
                _ => false,
            })) return false;
        if (item.Slot < 3 && (target == -1 ? !item.Active : item.Hero != target)) return false;
        return goal switch
        {
            "damage" => item.Slot is 0 or 3 or 7,
            "health" => item.Slot is 1 or 4 or 7,
            "armor" => item.Slot is 2 or 5 or 7,
            "gold" => item.Slot == 6,
            _ => false,
        };
    }

    public static double Score(VoidCrystalOption item, string goal, int target)
    {
        if (!Matches(item, goal, target)) return 0;
        if (goal == "balanced") return (item.Slot == 6 ? 4d : 1d) / item.Cost;
        if (goal == "power") return item.PowerGain / item.Cost;
        var share = target == -1 && item.Slot < 3 ? goal switch
        {
            "damage" => item.DamageShare,
            "health" => item.HealthShare,
            "armor" => item.ArmorShare,
            _ => 1,
        } : 1;
        return double.IsFinite(share) && share > 0 && share <= 1
            ? item.MultiplierGain * share / item.Cost : 0;
    }

    public static IEnumerable<VoidCrystalOption> Rank(IEnumerable<VoidCrystalOption> items, string goal, int target) =>
        items.Where(item => Score(item, goal, target) > 0).OrderByDescending(item => Score(item, goal, target))
            .ThenBy(item => goal == "balanced" ? item.Cost : 0)
            .ThenByDescending(item => goal == "balanced" ? 0 : item.PowerGain).ThenBy(item => item.Cost)
            .ThenBy(item => item.Hero).ThenBy(item => item.Slot);

    public static bool CanSpend(VoidCrystalOption expected, VoidCrystalOption current,
        double budget, double spent, double balance) =>
        expected != null && current != null && WholeAmount(budget) && budget > 0 &&
        WholeAmount(spent) && spent <= budget && WholeAmount(balance) &&
        WholeAmount(current.Cost) && current.Cost > 0 &&
        expected.Hero == current.Hero && expected.Slot == current.Slot &&
        expected.Level == current.Level && expected.Cost == current.Cost &&
        expected.Rarity == current.Rarity && expected.NativeEffect == current.NativeEffect &&
        expected.MultiplierGain == current.MultiplierGain && expected.PowerGain == current.PowerGain &&
        current.Cost <= budget - spent && current.Cost <= balance;

    public static bool Confirmed(int beforeLevel, int afterLevel, double beforeBalance, double afterBalance, double cost) =>
        beforeLevel >= 0 && afterLevel == beforeLevel + 1 &&
        WholeAmount(beforeBalance) && WholeAmount(afterBalance) && WholeAmount(cost) && cost > 0 &&
        beforeBalance - afterBalance == cost;
}
