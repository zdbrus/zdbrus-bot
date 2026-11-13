using System;

namespace Firebot.Core;

internal enum ScarabSpinCurrency { None, Noble, Pharaoh }

internal static class ScarabSpinPolicy
{
    public static ScarabSpinCurrency SelectCurrency(bool nobleVisible, bool pharaohVisible, bool allowPharaoh)
    {
        if (nobleVisible == pharaohVisible) return ScarabSpinCurrency.None;
        if (nobleVisible) return ScarabSpinCurrency.Noble;
        return allowPharaoh ? ScarabSpinCurrency.Pharaoh : ScarabSpinCurrency.None;
    }

    public static bool CanSpin(ScarabSpinCurrency currency, double balance, double cost)
        => currency != ScarabSpinCurrency.None && double.IsFinite(balance) && double.IsFinite(cost) &&
           balance >= cost && cost > 0 && Math.Truncate(balance) == balance && Math.Truncate(cost) == cost;

    public static bool SpentExactly(ScarabSpinCurrency currency, double before, double after, double cost)
        => CanSpin(currency, before, cost) && double.IsFinite(after) &&
           after >= 0 && after == before - cost;
}
