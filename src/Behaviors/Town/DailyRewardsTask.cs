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
    private const string ValueBundleTab = P.Menus + "/Store/submenuButtons/grid/valueBundleButton";
    private const string ScarabTokenButton =
        P.Popups + "/ScarabGameShop/bg/submenus/sale/items/scarabGameShopFreeTokenInteraction/claimBg/purchaseButton";
    private const string ScarabTokenClaimed =
        P.Popups + "/ScarabGameShop/bg/submenus/sale/items/scarabGameShopFreeTokenInteraction/claimBg/claimedBG";
    private const string CheckInButton =
        P.Menus + "/Store/bg/submenus/dailyRewards/Viewport/transparentFrame/checkIn";
    private static readonly string[] NotificationNames =
        { "MysteryBox", "CheckIn", "ScarabGameShopFreeToken", "OraclesGift" };
    private static readonly string[] OtherNotificationNames =
        { "CheckIn", "ScarabGameShopFreeToken", "OraclesGift" };
    private readonly Func<DateTime> _now;
    private DateTime _mysteryRetryAfter;
    private bool _mysteryNeedsRetry;

    public DailyRewardsTask() : this(() => DateTime.Now) { }

    internal DailyRewardsTask(Func<DateTime> now)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    internal override IEnumerable<string> HandledNotificationNames => NotificationNames;
    internal override IEnumerable<KeyValuePair<string, DateTime>> RetryDeadlines
    {
        get
        {
            // An overdue unconfirmed claim remains pending until it is revisited.
            if (_mysteryNeedsRetry || _mysteryRetryAfter > _now())
                yield return new KeyValuePair<string, DateTime>(
                    _mysteryNeedsRetry ? "MysteryRetry" : "MysteryQuiet", _mysteryRetryAfter);
        }
    }

    internal override void RestoreRetryDeadline(string name, DateTime when)
    {
        if (name != "MysteryRetry" && name != "MysteryQuiet") return;
        _mysteryRetryAfter = when;
        _mysteryNeedsRetry = name == "MysteryRetry";
    }

    protected override bool IsNotificationVisibleCore()
        => IsEnabled && _now() >= BubbleQuietUntil &&
           ((_now() >= _mysteryRetryAfter && Ui.Visible(P.Notifications + "/MysteryBox")) ||
            OtherNotificationNames.Any(name => Ui.Visible(P.Notifications + "/" + name)));

    private void Schedule(DateTime when)
    {
        NextRunTime = _mysteryNeedsRetry && _mysteryRetryAfter < when ? _mysteryRetryAfter : when;
    }

    private static IEnumerator ClaimWithVisibleState(string path, string claimedPath, string label,
        Action<bool> result)
    {
        var wasClaimed = Ui.Visible(claimedPath);
        var rewardWasVisible = Ui.Visible(P.RewardsClose);
        yield return Ui.ClickUntil(Ui.Find(path),
            () => (!wasClaimed && Ui.Visible(claimedPath)) || (Ui.Visible(path) && !Ui.Clickable(path)) ||
                  (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
            result, label, 1.5f);
    }

    public override IEnumerator Execute()
    {
        var mysteryReady = _now() >= _mysteryRetryAfter;
        var mysteryNotification = mysteryReady && Ui.Visible(P.Notifications + "/MysteryBox");
        var notificationTriggered = mysteryNotification ||
                                    OtherNotificationNames.Any(name => Ui.Visible(P.Notifications + "/" + name));
        var verifiedStateChanges = 0;
        var unconfirmedClaims = 0;
        if (mysteryReady && (!notificationTriggered || mysteryNotification || _mysteryNeedsRetry))
        {
            yield return Notifications.MysteryBox;
            yield return new WaitForSeconds(MenuOpenDelay);
            if (!Ui.Visible(MysteryBoxButton) && !Ui.Visible(MysteryBoxClaimed) && Ui.Clickable(ValueBundleTab))
            {
                yield return Ui.Click(ValueBundleTab, MenuOpenDelay);
                yield return Ui.WaitUntil(() => Ui.Visible(MysteryBoxButton) || Ui.Visible(MysteryBoxClaimed),
                    _ => { }, 3f);
            }
            var confirmed = false;
            var alreadyClaimed = Ui.Visible(MysteryBoxClaimed);
            if (alreadyClaimed)
                Logger.Info("[Daily Rewards] Mystery Box already claimed; waiting for the next one.");
            else if (Ui.Clickable(MysteryBoxButton) &&
                     !Ui.HasPriceMarker(Ui.Find(MysteryBoxButton), allowFreePriceHolder: true))
            {
                var rewardWasVisible = Ui.Visible(P.RewardsClose);
                var clicked = false;
                yield return Ui.ClickIf(MysteryBoxButton,
                    () => !Ui.Visible(MysteryBoxClaimed) &&
                          !Ui.HasPriceMarker(Ui.Find(MysteryBoxButton), allowFreePriceHolder: true),
                    value => clicked = value, 1.5f, "Mystery Box");
                if (clicked)
                    yield return Ui.WaitUntil(
                        () => Ui.Visible(MysteryBoxClaimed) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                        value => confirmed = value, 2f);
                if (!confirmed) Logger.Warning("[Daily Rewards] The Mystery Box claim could not be confirmed.");
            }
            else
                Logger.Warning("[Daily Rewards] The Mystery Box could not be found or claimed; trying again later.");
            yield return Ui.CloseRewards();
            if (confirmed) verifiedStateChanges++;
            else if (!alreadyClaimed) unconfirmedClaims++;
            var reset = Store.MysteryBoxNextRunTime;
            var now = _now();
            if (confirmed || alreadyClaimed)
            {
                _mysteryNeedsRetry = false;
                var quietUntil = now.AddHours(3);
                _mysteryRetryAfter = reset > now && reset < quietUntil ? reset : quietUntil;
                Schedule(reset > now ? reset : now.AddHours(24));
            }
            else
            {
                _mysteryNeedsRetry = true;
                _mysteryRetryAfter = now.AddHours(1);
                Schedule(_mysteryRetryAfter);
            }
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
            Schedule(reset > _now() ? reset : _now().AddHours(24));
            yield return Store.Close;
        }

        if (!PlayerStats.TryGetCharacterLevel(out var level))
        {
            Schedule(_now().AddSeconds(BotSettings.ScanInterval));
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
            Schedule(reset > _now() && reset < _now().AddDays(2)
                ? reset
                : _now().AddHours(24));
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
            BubbleQuietUntil = _now().AddHours(3);
        }
        else
        {
            unconfirmedClaims++;
            BubbleQuietUntil = _now().AddHours(1);
        }

        var giftReset = OracleStore.NextRunTime;
        Schedule(giftReset > _now() ? giftReset : _now().AddHours(24));
        yield return OracleStore.Close;
        Logger.Info($"[Daily Rewards] Claim controls changed for {verifiedStateChanges} reward(s); " +
                    $"{unconfirmedClaims} claim(s) unconfirmed. Reward receipt is not independently verified.");
    }
}
