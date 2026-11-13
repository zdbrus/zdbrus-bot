namespace Firebot.Core;

internal static class BattlePassPolicy
{
    public static bool ShouldScanRewards(bool notificationFound, bool notificationVisible)
        => !notificationFound || notificationVisible;
}
