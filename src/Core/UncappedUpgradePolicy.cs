using System;

namespace Firebot.Core;

internal enum UncappedUpgradeMode
{
    Balanced,
    Focus
}

internal static class UncappedUpgradePolicy
{
    public static bool TryParse(string value, out UncappedUpgradeMode mode)
    {
        mode = UncappedUpgradeMode.Balanced;
        if (string.Equals(value?.Trim(), "balanced", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(value?.Trim(), "focus", StringComparison.OrdinalIgnoreCase)) return false;
        mode = UncappedUpgradeMode.Focus;
        return true;
    }

    public static bool ContinueAfterTarget(UncappedUpgradeMode mode, bool available)
        => mode == UncappedUpgradeMode.Balanced || !available;
}
