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
    public override GameFeature RequiredFeature => GameFeature.Store;

    private const float MenuOpenDelay = 2f;
    private const string OracleGiftButton =
        P.Menus + "/OracleStore/bg/submenus/extremeValueBundles/items/oraclesGift/Graphics/purchaseButton";
    private const string OracleGiftClaimed =
        P.Menus + "/OracleStore/bg/submenus/extremeValueBundles/items/oraclesGift/Graphics/claimedBG";
    private const string MysteryBoxButton =
        P.Menus + "/Store/bg/submenus/valueBundleDaily/Scroll View/Viewport/bundles/mysteryBox/Graphics/purchaseButton";
    private const string MysteryBoxClaimed =
        P.Menus + "/Store/bg/submenus/valueBundleDaily/Scroll View/Viewport/bundles/mysteryBox/Graphics/claimedBG";
    private const string ValueBundleTab = P.Menus + "/Store/submenuButtons/grid/valueBundleDailyButton";
    private const string ScarabTokenButton =
        P.Popups + "/ScarabGameShop/bg/submenus/extremeValueBundlesDaily/Scroll View/Viewport/items/scarabGameShopFreeTokenInteraction/claimBg/purchaseButton";
    private const string ScarabTokenClaimed =
        P.Popups + "/ScarabGameShop/bg/submenus/extremeValueBundlesDaily/Scroll View/Viewport/items/scarabGameShopFreeTokenInteraction/claimBg/claimedBG";
    private const string ScarabDailyTab = P.Popups + "/ScarabGameShop/bg/submenuButtons/dailyDeals/button";
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
        // The shop screens finish their open animation after the button is already in the tree; give a visible
        // button a moment to become clickable before reporting it unavailable.
        if (Ui.Visible(path) && !Ui.Clickable(path)) yield return Ui.WaitUntil(() => Ui.Clickable(path), _ => { }, 3f);
        yield return Ui.ClickUntil(Ui.Find(path),
            () => (!wasClaimed && Ui.Visible(claimedPath)) || (Ui.Visible(path) && !Ui.Clickable(path)) ||
                  (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
            result, label, 1.5f);
    }

    private const string StoreRoot = P.Menus + "/Store";
    private const string StoreTabs = StoreRoot + "/submenuButtons/grid";

    /// <summary>
    ///     The Mystery Box item, wherever the store shows it: the open tab first, then each tab named after the
    ///     value or extreme-value bundles.
    /// </summary>
    private static IEnumerator FindMysteryBox(Action<string> found)
    {
        // The bubble may open the bundle screen itself rather than the store: whatever it opened is searched too.
        var box = Ui.FindVisibleDescendant(StoreRoot + "/bg/submenus", "mysteryBox") ??
                  Ui.FindVisibleDescendant(P.Popups, "mysteryBox") ??
                  Ui.FindVisibleDescendant(P.Menus, "mysteryBox");
        if (box == null)
            foreach (var tab in Ui.VisibleChildren(StoreTabs).ToList())
            {
                var name = tab.name.ToLowerInvariant();
                if (!name.Contains("extreme") && !name.Contains("valuebundle")) continue;
                var path = StoreTabs + "/" + tab.name;
                if (!Ui.Clickable(path)) continue;
                yield return Ui.Click(path, MenuOpenDelay);
                yield return Ui.WaitUntil(
                    () => Ui.FindVisibleDescendant(StoreRoot + "/bg/submenus", "mysteryBox") != null, _ => { }, 3f);
                box = Ui.FindVisibleDescendant(StoreRoot + "/bg/submenus", "mysteryBox");
                if (box != null) break;
            }

        found(box);
    }

    /// <summary>The box's claim button or claimed marker, under either layout the game has used.</summary>
    private static string MysteryBoxControl(string box, string control)
    {
        foreach (var holder in new[] { "/Graphics/", "/claimBg/", "/Graphics/claimBg/", "/" })
            if (Ui.Find(box + holder + control) != null)
                return box + holder + control;
        return box + "/Graphics/" + control;
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

            // Game 9.1 moved the free Mystery Box to the Extreme Value Bundle screen (issue #8). The box is found by
            // name on whichever store tab shows it, trying the bundle tabs in turn.
            var mysteryButton = MysteryBoxButton;
            var mysteryClaimed = MysteryBoxClaimed;
            if (!Ui.Visible(mysteryButton) && !Ui.Visible(mysteryClaimed))
            {
                string box = null;
                yield return FindMysteryBox(value => box = value);
                if (box != null)
                {
                    mysteryButton = MysteryBoxControl(box, "purchaseButton");
                    mysteryClaimed = MysteryBoxControl(box, "claimedBG");
                }
            }

            var confirmed = false;
            var alreadyClaimed = Ui.Visible(mysteryClaimed);
            if (alreadyClaimed)
                Logger.Info("[Daily Rewards] Mystery Box already claimed; waiting for the next one.");
            else if (Ui.Clickable(mysteryButton) &&
                     !Ui.HasPriceMarker(Ui.Find(mysteryButton), allowFreePriceHolder: true))
            {
                var rewardWasVisible = Ui.Visible(P.RewardsClose);
                var clicked = false;
                yield return Ui.ClickIf(mysteryButton,
                    () => !Ui.Visible(mysteryClaimed) &&
                          !Ui.HasPriceMarker(Ui.Find(mysteryButton), allowFreePriceHolder: true),
                    value => clicked = value, 1.5f, "Mystery Box");
                if (clicked)
                    yield return Ui.WaitUntil(
                        () => Ui.Visible(mysteryClaimed) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
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
            if (!Ui.Visible(ScarabTokenButton) && !Ui.Visible(ScarabTokenClaimed) && Ui.Clickable(ScarabDailyTab))
            {
                yield return Ui.Click(ScarabDailyTab, MenuOpenDelay);
                yield return Ui.WaitUntil(() => Ui.Visible(ScarabTokenButton) || Ui.Visible(ScarabTokenClaimed),
                    _ => { }, 3f);
            }
            yield return ClaimWithVisibleState(ScarabTokenButton, ScarabTokenClaimed, "Scarab free token",
                value => confirmed = value);
            yield return Ui.CloseRewards();
            if (confirmed) verifiedStateChanges++;
            else
            {
                unconfirmedClaims++;
                // A lit bubble with no claimable button would otherwise reopen the shop every few minutes.
                BubbleQuietUntil = _now().AddHours(3);
            }
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
