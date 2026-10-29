using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Features.Town.Oracle;
using Firebot.GameModel.Shared;
using UnityEngine;

namespace Firebot.Behaviors.Town;

public class DailyRewardsTask : BotTask
{
    private const float MenuOpenDelay = 2f;
    private static readonly string[] NotificationNames =
        { "MysteryBox", "CheckIn", "ScarabGameShopFreeToken", "OraclesGift" };

    internal override IEnumerable<string> HandledNotificationNames => NotificationNames;

    protected override bool IsNotificationVisibleCore()
        => IsEnabled && DateTime.Now >= BubbleQuietUntil &&
           NotificationNames.Any(name => Ui.Visible(P.Notifications + "/" + name));

    public override IEnumerator Execute()
    {
        var notificationTriggered = NotificationNames.Any(name => Ui.Visible(P.Notifications + "/" + name));
        if (!notificationTriggered || Ui.Visible(P.Notifications + "/MysteryBox"))
        {
            yield return Notifications.MysteryBox;
            yield return new WaitForSeconds(MenuOpenDelay);
            yield return Store.ClaimMysteryBox;
            NextRunTime = Store.MysteryBoxNextRunTime;
            yield return Store.Close;
        }

        if (!notificationTriggered || Ui.Visible(P.Notifications + "/CheckIn"))
        {
            yield return Notifications.CheckIn;
            yield return new WaitForSeconds(MenuOpenDelay);
            yield return Store.ClaimCheckIn;
            NextRunTime = Store.CheckInNextRunTime;
            yield return Store.Close;
        }

        if (!PlayerStats.TryGetCharacterLevel(out var level))
        {
            NextRunTime = DateTime.Now.AddSeconds(BotSettings.ScanInterval);
            yield break;
        }

        if (level >= LevelRequirements.ScarabGameShop &&
            (!notificationTriggered || Ui.Visible(P.Notifications + "/ScarabGameShopFreeToken")))
        {
            yield return Notifications.ScarabGameShopFreeToken;
            yield return new WaitForSeconds(MenuOpenDelay);
            yield return Store.ClaimScarabGameShopFreeToken;
            NextRunTime = Store.ScarabGameShopFreeTokenNextRunTime;
            yield return Store.CloseScarabGameShop;
        }

        // Fail-closed here, unlike BotTask.IsLevelLocked: the Oracle gift is a bonus on top of the check-in,
        // and the menu only exists at LevelRequirements.Oracle — with no level read there is no reason to
        // trust that it is there.
        if (level < LevelRequirements.Oracle ||
            (notificationTriggered && !Ui.Visible(P.Notifications + "/OraclesGift")))
            yield break;

        yield return Notifications.OraclesGift;
        yield return new WaitForSeconds(MenuOpenDelay);
        yield return OracleStore.ClaimGift;
        NextRunTime = OracleStore.NextRunTime;
        yield return OracleStore.Close;
    }
}
