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
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Town;

public class DailyRewardsTask : BotTask
{
    private const float MenuOpenDelay = 2f;
    private const string OracleGiftButton =
        P.Menus + "/OracleStore/bg/submenus/extremeValueBundles/items/oraclesGift/Graphics/purchaseButton";
    private const string OracleGiftClaimed =
        P.Menus + "/OracleStore/bg/submenus/extremeValueBundles/items/oraclesGift/Graphics/claimedBG";
    private const string MysteryBoxButton =
        P.Menus + "/Store/bg/submenus/extremeValueBundles/bundles/mysteryBox/Graphics/purchaseButton";
    private const string MysteryBoxClaimed =
        P.Menus + "/Store/bg/submenus/extremeValueBundles/bundles/mysteryBox/Graphics/claimedBG";
    private const string ScarabTokenButton =
        P.Popups + "/ScarabGameShop/bg/submenus/sale/items/scarabGameShopFreeTokenInteraction/claimBg/purchaseButton";
    private const string ScarabTokenClaimed =
        P.Popups + "/ScarabGameShop/bg/submenus/sale/items/scarabGameShopFreeTokenInteraction/claimBg/claimedBG";
    private const string CheckInButton =
        P.Menus + "/Store/bg/submenus/dailyRewards/Viewport/transparentFrame/checkIn";
    private static readonly string[] NotificationNames =
        { "MysteryBox", "CheckIn", "ScarabGameShopFreeToken", "OraclesGift" };

    internal override IEnumerable<string> HandledNotificationNames => NotificationNames;

    protected override bool IsNotificationVisibleCore()
        => IsEnabled && DateTime.Now >= BubbleQuietUntil &&
           NotificationNames.Any(name => Ui.Visible(P.Notifications + "/" + name));

    private static IEnumerator ClaimWithVisibleState(string path, string claimedPath, string label,
        Action<bool> result)
    {
        var wasClaimed = Ui.Visible(claimedPath);
        var rewardWasVisible = Ui.Visible(P.RewardsClose);
        yield return Ui.ClickUntil(Ui.Find(path),
            () => (!wasClaimed && Ui.Visible(claimedPath)) || !Ui.Clickable(path) ||
                  (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
            result, label, 1.5f);
    }

    public override IEnumerator Execute()
    {
        var notificationTriggered = NotificationNames.Any(name => Ui.Visible(P.Notifications + "/" + name));
        var verifiedStateChanges = 0;
        var unconfirmedClaims = 0;
        if (!notificationTriggered || Ui.Visible(P.Notifications + "/MysteryBox"))
        {
            yield return Notifications.MysteryBox;
            yield return new WaitForSeconds(MenuOpenDelay);
            var confirmed = false;
            yield return ClaimWithVisibleState(MysteryBoxButton, MysteryBoxClaimed, "Mystery Box", value => confirmed = value);
            yield return Ui.CloseRewards();
            if (confirmed) verifiedStateChanges++;
            else unconfirmedClaims++;
            var reset = Store.MysteryBoxNextRunTime;
            NextRunTime = reset > DateTime.Now ? reset : DateTime.Now.AddHours(24);
            yield return Store.Close;
        }

        if (!notificationTriggered || Ui.Visible(P.Notifications + "/CheckIn"))
        {
            yield return Notifications.CheckIn;
            yield return new WaitForSeconds(MenuOpenDelay);
            var confirmed = false;
            yield return ClaimWithVisibleState(CheckInButton, null, "Check-in", value => confirmed = value);
            yield return Ui.CloseRewards();
            if (confirmed) verifiedStateChanges++;
            else unconfirmedClaims++;
            var reset = Store.CheckInNextRunTime;
            NextRunTime = reset > DateTime.Now ? reset : DateTime.Now.AddHours(24);
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
            var confirmed = false;
            yield return ClaimWithVisibleState(ScarabTokenButton, ScarabTokenClaimed, "Scarab free token",
                value => confirmed = value);
            yield return Ui.CloseRewards();
            if (confirmed) verifiedStateChanges++;
            else unconfirmedClaims++;
            var reset = Store.ScarabGameShopFreeTokenNextRunTime;
            NextRunTime = reset > DateTime.Now && reset < DateTime.Now.AddDays(2)
                ? reset
                : DateTime.Now.AddHours(24);
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
        var claimConfirmed = false;
        yield return ClaimWithVisibleState(OracleGiftButton, OracleGiftClaimed, "Oracle's gift",
            value => claimConfirmed = value);
        yield return Ui.CloseRewards();
        if (claimConfirmed)
        {
            verifiedStateChanges++;
            BubbleQuietUntil = DateTime.Now.AddHours(3);
        }
        else
        {
            unconfirmedClaims++;
            BubbleQuietUntil = DateTime.Now.AddHours(1);
        }

        var giftReset = OracleStore.NextRunTime;
        NextRunTime = giftReset > DateTime.Now ? giftReset : DateTime.Now.AddHours(24);
        yield return OracleStore.Close;
        Logger.Info($"[Daily Rewards] Claim controls changed for {verifiedStateChanges} reward(s); " +
                    $"{unconfirmedClaims} claim(s) unconfirmed. Reward receipt is not independently verified.");
    }
}
