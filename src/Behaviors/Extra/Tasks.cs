using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebot.Core;
using Firebot.Core.Tasks;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

// Features added in Zdbrus Bot. Every buy/spend button is clicked only when the game has it enabled AND the
// matching counter shows enough of the currency. Nothing here ever spends gems.

public class MailTask : BotTask
{
    private const string Mail = P.Popups + "/SystemMail";
    private const string List = Mail + "/bg/mailListBg/Scroll View/Viewport/mailList";
    private const string Claim = Mail + "/bg/mailFullView/mailMessage/rewardsObj/claimButton";
    private const string Delete = Mail + "/bg/mailFullView/deleteButton";
    private MelonPreferences_Entry<bool> _deleteClaimed;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _deleteClaimed = c.CreateEntry("delete_after_claim", false, "Delete After Claim",
            "Deletes a mail once its rewards are claimed.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddMinutes(30);
        yield return Ui.Click(P.MailButton, 1.5f);
        yield return Ui.WaitFor(Mail);
        if (!Ui.Visible(Mail)) yield break;

        var count = Ui.VisibleChildren(List).Count();
        var claimsWithStateChange = 0;
        var claimsUnconfirmed = 0;
        var deleted = 0;
        var kept = 0;
        for (var i = 0; i < 40 && i < count + kept; i++)
        {
            // With delete on, handled mails leave the list, so the first one that is still there is next.
            var item = Ui.VisibleChildren(List).Skip(kept).FirstOrDefault();
            if (item == null) break;
            yield return Ui.Click(item, 1f);

            var claimed = true;
            if (Ui.Clickable(Claim))
            {
                var rewardWasVisible = Ui.Visible(P.RewardsClose);
                var confirmed = false;
                yield return Ui.ClickUntil(Ui.Find(Claim),
                    () => !Ui.Clickable(Claim) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                    value => confirmed = value, "mail reward claim", 1.5f);
                yield return Ui.CloseRewards();
                if (confirmed) claimsWithStateChange++;
                else { claimsUnconfirmed++; claimed = false; }
            }

            // A mail with nothing to claim (an announcement, or one already claimed) is deleted as well; one whose
            // claim failed is kept so its rewards are not thrown away.
            if (_deleteClaimed.Value && claimed && Ui.Clickable(Delete))
            {
                var before = Ui.VisibleChildren(List).Count();
                yield return Ui.Click(Delete, 1f, "delete mail");
                if (Ui.VisibleChildren(List).Count() < before) { deleted++; continue; }
            }

            kept++;
        }

        Logger.Info($"[Mail] Claimed {claimsWithStateChange}, unconfirmed {claimsUnconfirmed}, deleted {deleted}, " +
                    "kept " + kept + ". Reward receipt is not independently verified.");
        // With nothing to claim or delete the mailbox is not worth reopening every half hour.
        if (claimsWithStateChange + deleted == 0) NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.Click(Mail + "/bg/closeButton");
    }
}

public class TavernTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/BeerExchange";

    private const string Tavern = P.Menus + "/Tavern/helpCanvas";
    private const string Market = P.Popups + "/TavernMarket/bg";
    private const string Tokens = Tavern + "/counters/currencyInteraction (GameToken)/quantity";
    private const string Play = Tavern + "/bottomUI/playButton";
    private const string AutoPick = Tavern + "/bottomRightUI/autoPickToggle";
    private MelonPreferences_Entry<bool> _buyTokens;
    private MelonPreferences_Entry<bool> _playCards;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _buyTokens = c.CreateEntry("buy_tokens_with_beer", true, "Buy Tokens With Beer",
            "Trades beer for game tokens in the tavern market (never gems).");
        _playCards = c.CreateEntry("play_cards", true, "Play Cards",
            "Plays the tavern cards with your tokens, using the game's auto pick.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddMinutes(60);
        yield return Ui.OpenTownBuilding("tavern");
        yield return Ui.Click(P.Popups + "/TavernSelection/bg/tavern", 2f);
        if (!Ui.Visible(Tavern)) yield break;

        if (_buyTokens.Value)
        {
            yield return Ui.Click(Tavern + "/actionButtons/shop", 1.5f);
            for (var i = 0; i < 20 && Ui.Visible(Market); i++)
            {
                var beer = Ui.NumberAt(Market + "/lower/counters/currencyInteraction (Beer)/quantity");
                var tokensBefore = Ui.NumberAt(Tokens);
                var cost = Ui.NumberAt(Market + "/items/gameTokenWithBeer/purchaseButtonOffer/currencyHolder/purchaseCost");
                if (cost <= 0 || beer < cost) break;
                var beerBefore = beer;
                var bought = false;
                var offer = Market + "/items/gameTokenWithBeer/purchaseButtonOffer";
                yield return Ui.ClickUntil(Ui.Find(offer),
                    () => Ui.NumberAt(Market + "/lower/counters/currencyInteraction (Beer)/quantity") < beerBefore ||
                          Ui.NumberAt(Tokens) > tokensBefore,
                    value => bought = value, "tavern token purchase", 1.5f);
                if (!bought)
                {
                    Logger.Warning("[Tavern] Token purchase made no observable currency change; stopping purchases.");
                    break;
                }
                yield return Ui.CloseRewards();
            }

            yield return Ui.Click(Market + "/closeButton");
        }

        if (_playCards.Value)
        {
            if (Ui.Text(AutoPick + "/text").Equals("Manual", StringComparison.OrdinalIgnoreCase))
                yield return Ui.Click(AutoPick);

            var plays = 0;
            var rng = new System.Random();
            while (plays < 20 && Ui.NumberAt(Tokens) > 0 && Ui.Clickable(Play))
            {
                var tokensBefore = Ui.NumberAt(Tokens);
                yield return Ui.Click(Play, 2f);
                // In manual mode the game waits for a card to be picked.
                var cards = Ui.VisibleChildren(Tavern + "/cardHolder").Where(Ui.Clickable).ToList();
                if (cards.Count > 0) yield return Ui.Click(cards[rng.Next(cards.Count)], 3f, "tavern card");
                yield return Ui.CloseRewards();
                if (Ui.NumberAt(Tokens) >= tokensBefore)
                {
                    Logger.Warning("[Tavern] A play did not reduce the token counter; stopping.");
                    break;
                }
                plays++;
            }

            if (plays == 20) NextRunTime = DateTime.Now.AddMinutes(2);
        }

        yield return Ui.Click(P.Menus + "/Tavern/closeButton");
    }
}

public class ExoticMerchantTask : BotTask
{
    private const string M = P.Menus + "/ExoticMerchant/submenus";
    private const string ExoticCoins = P.Menus + "/ExoticMerchant/counters/currencyInteraction (ExoticCoin)/quantity";
    private const string SellGrid = M + "/bg/sellItemsSubmenu/Scroll View/Viewport/Content/productGrid";
    private const string Upgrades = M + "/bg/upgradesSubmenu";
    private const string NextTree = M + "/bg/upgradesSubmenu/navigation/goForthTree";
    private const string GearGrid = M + "/bg/emblemMarketSubmenu/categories/gearCategory/scrollView/Viewport/Content/productGrid";
    private MelonPreferences_Entry<int> _sellMode;
    private MelonPreferences_Entry<bool> _buyUpgrades;
    private MelonPreferences_Entry<bool> _buyChests;
    private MelonPreferences_Entry<string> _priority;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _sellMode = c.CreateEntry("sell_mode", 2, "Sell Items",
            "0 = sell nothing, 1 = scrolls only, 2 = everything except gold items, 3 = everything.");
        _buyUpgrades = c.CreateEntry("buy_upgrades", true, "Buy Exotic Upgrades",
            "Spends exotic coins in priority order, filling an available tier before the next.");
        _priority = c.CreateEntry("priority",
            "Prestigious,Firestone Finder,Raining gold,Heroes' damage,Team bonus,All main attributes,Firestone effect," +
            "Damage specialization,Leadership,Guardian power,Energy Heroes,Mana Heroes,Rage Heroes," +
            "Fist fight,Magic spells,Precision,Skip stage,Skip wave,Healer specialization," +
            "Tank specialization,Attribute health,Attribute armor",
            "Priority", "Exotic upgrades to fill in order. Level-locked upgrades are skipped until unlocked.");
        _buyChests = c.CreateEntry("buy_emblem_chests", true, "Buy Emblem Chests",
            "Spends emblems on gear chests, best chest first.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(2);
        yield return Ui.OpenTownBuilding("exoticMerchant");
        if (!Ui.Visible(M)) yield break;

        var clicks = 0;
        if (_sellMode.Value > 0)
        {
            yield return Ui.Click(M + "/submenuButtons/sellItems", 1.5f);
            foreach (var item in Ui.VisibleChildren(SellGrid).ToList())
            {
                var name = Ui.Text(item.Find("itemName"));
                if (!ShouldSell(name)) continue;
                var button = item.Find("sellButton");
                while (clicks < 60 && Ui.Clickable(button) && Ui.NumberOf(item.Find("itemBg/quantity")) > 0)
                {
                    var quantityBefore = Ui.NumberOf(item.Find("itemBg/quantity"));
                    var coinsBefore = Ui.NumberAt(ExoticCoins);
                    var sold = false;
                    yield return Ui.ClickUntil(button,
                        () => PurchaseObservation.Decreased(quantityBefore, Ui.NumberOf(item.Find("itemBg/quantity"))) ||
                              PurchaseObservation.Increased(coinsBefore, Ui.NumberAt(ExoticCoins)),
                        value => sold = value, $"sell {name}", 0.6f);
                    if (!sold)
                    {
                        Logger.Warning($"[Exotic Merchant] Stopping sales for {name}: quantity or coin change was not confirmed.");
                        break;
                    }
                    clicks++;
                }
            }
        }

        if (_buyUpgrades.Value)
        {
            var retry = false;
            yield return BuyRankedUpgrades(() => retry = true);
            if (retry) NextRunTime = DateTime.Now.AddMinutes(2);
        }

        if (_buyChests.Value)
        {
            yield return Ui.Click(M + "/submenuButtons/emblemMarket", 1.5f);
            yield return Ui.Click(M + "/bg/emblemMarketSubmenu/categoryButtons/gear", 1.2f);
            var purchaseStalled = false;
            for (var i = 3; i >= 0; i--)
            {
                var buy = GearGrid + $"/exoticMerchantEmblemGearChest ({i})/purchaseButton";
                for (var n = 0; n < 5 && Ui.Clickable(buy); n++)
                {
                    var rewardWasVisible = Ui.Visible(P.RewardsClose);
                    var purchased = false;
                    yield return Ui.ClickUntil(Ui.Find(buy),
                        () => !Ui.Clickable(buy) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                        value => purchased = value, "emblem gear chest", 1.5f);
                    if (!purchased)
                    {
                        Logger.Warning("[Exotic Merchant] Chest purchase made no visible state change; stopping chest purchases.");
                        purchaseStalled = true;
                        break;
                    }
                    yield return Ui.CloseRewards();
                }

                if (purchaseStalled) break;
            }
        }

        if (clicks >= 60) NextRunTime = DateTime.Now.AddMinutes(3);
        yield return Ui.Click(P.Menus + "/ExoticMerchant/closeButton");
    }

    private static List<ExoticUpgradesInteraction> VisibleUpgrades()
    {
        var result = new List<ExoticUpgradesInteraction>();
        var panel = Ui.Find(Upgrades);
        if (panel == null || !panel.gameObject.activeInHierarchy) return result;
        foreach (var item in panel.GetComponentsInChildren<ExoticUpgradesInteraction>(true))
            if (item != null && item.gameObject.activeInHierarchy) result.Add(item);
        return result;
    }

    private static bool IsMaxed(ExoticUpgradesInteraction item)
        => item != null && item.upgrade != null &&
           ((ExoticUpgrade.levelCap > 0 && item.upgrade.level >= ExoticUpgrade.levelCap) ||
            (item.maxLevelObj != null && item.maxLevelObj.activeInHierarchy));

    private IEnumerator BuyRankedUpgrades(Action retrySoon)
    {
        var coins = Ui.NumberAt(ExoticCoins);
        if (coins < 0)
        {
            Logger.Warning("[Exotic Merchant] Exotic coin count is unreadable; upgrades were left untouched.");
            yield break;
        }
        if (coins == 0)
        {
            Logger.Info("[Exotic Merchant] Skipping upgrades: no exotic coins available.");
            yield break;
        }

        yield return Ui.Click(M + "/submenuButtons/upgrades", 1.5f);
        yield return Ui.WaitFor(Upgrades);
        var order = (_priority.Value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var purchases = 0;
        for (var tree = 0; tree < 8; tree++)
        {
            var items = VisibleUpgrades();
            if (items.Count == 0 || items.Any(item => item.upgrade == null))
            {
                Logger.Warning("[Exotic Merchant] Upgrade data is unavailable; lower tiers were left untouched.");
                yield break;
            }

            var treeId = items[0].upgrade.exoticTree;
            if (items.Any(item => item.upgrade.exoticTree != treeId))
            {
                Logger.Warning("[Exotic Merchant] Upgrade tree changed while reading it; no purchase attempted.");
                yield break;
            }

            var ranked = new List<(ExoticUpgradesInteraction item, string name, int code)>();
            foreach (var item in items)
            {
                var name = Ui.Text(item.nameText != null ? item.nameText.transform : null);
                if (string.IsNullOrWhiteSpace(name)) name = item.upgrade.name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    Logger.Warning("[Exotic Merchant] An upgrade name is unreadable; lower tiers were left untouched.");
                    yield break;
                }
                ranked.Add((item, name, item.upgrade.upgradeCode));
            }

            foreach (var entry in ranked.OrderBy(item => ExoticUpgradePolicy.Rank(order, item.name)))
            {
                while (true)
                {
                    var item = entry.item;
                    var upgrade = item.upgrade;
                    if (upgrade == null || upgrade.exoticTree != treeId || upgrade.upgradeCode != entry.code)
                    {
                        Logger.Warning($"[Exotic Merchant] {entry.name}: the upgrade changed; lower tiers were left untouched.");
                        retrySoon();
                        yield break;
                    }

                    var maxed = IsMaxed(item);
                    if (!maxed && (ExoticUpgrade.levelCap <= 0 || upgrade.level < 0))
                    {
                        Logger.Warning($"[Exotic Merchant] {entry.name}: upgrade level is invalid; lower tiers were left untouched.");
                        yield break;
                    }
                    var locked = item.reachLevelTextObj != null && item.reachLevelTextObj.activeInHierarchy;
                    var button = item.upgradeButton != null ? item.upgradeButton.transform : null;
                    var hasButton = button != null && button.gameObject.activeInHierarchy;
                    var cost = Ui.NumberOf(item.levelUpCostText != null ? item.levelUpCostText.transform : null);
                    var balance = Ui.NumberAt(ExoticCoins);
                    var action = ExoticUpgradePolicy.Decide(maxed, locked, hasButton, Ui.Clickable(button),
                        purchases >= 90, cost, balance);
                    if (action == ExoticUpgradeAction.Advance) break;
                    if (action != ExoticUpgradeAction.Buy)
                    {
                        if (action == ExoticUpgradeAction.RetrySoon) retrySoon();
                        Logger.Info($"[Exotic Merchant] Holding at {entry.name}: cost {cost}, coins {balance}; lower tiers stay untouched.");
                        yield break;
                    }

                    var levelBefore = upgrade.level;
                    var confirmed = false;
                    yield return Ui.ClickUntil(button,
                        () => (item.upgrade != null && item.upgrade.exoticTree == treeId &&
                               item.upgrade.upgradeCode == entry.code && item.upgrade.level > levelBefore) ||
                              PurchaseObservation.Decreased(balance, Ui.NumberAt(ExoticCoins)) ||
                              PurchaseObservation.ChangedPrice(cost,
                                  Ui.NumberOf(item.levelUpCostText != null ? item.levelUpCostText.transform : null)),
                        value => confirmed = value, entry.name, 0.8f);
                    if (!confirmed || Ui.MissingCurrency)
                    {
                        Logger.Warning($"[Exotic Merchant] Stopping at {entry.name}: purchase was not confirmed.");
                        yield break;
                    }
                    purchases++;
                }
            }

            if (items.Any(item => item.upgrade == null || item.upgrade.exoticTree != treeId || !IsMaxed(item)))
                yield break;
            if (!Ui.Clickable(NextTree)) yield break;

            var advanced = false;
            yield return Ui.ClickUntil(Ui.Find(NextTree),
                () =>
                {
                    var next = VisibleUpgrades();
                    return next.Count > 0 && next[0].upgrade != null && next[0].upgrade.exoticTree != treeId;
                },
                value => advanced = value, "next exotic tree", 1.2f);
            if (!advanced)
            {
                if (Ui.Visible(P.Popups + "/GenericMessage/bg"))
                    yield return Ui.Click(P.Popups + "/GenericMessage/bg/closeButton");
                yield break;
            }
            if (tree == 7) retrySoon();
        }
    }

    private bool ShouldSell(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var isScroll = name.IndexOf("scroll", StringComparison.OrdinalIgnoreCase) >= 0;
        var isGold = name.IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("midas", StringComparison.OrdinalIgnoreCase) >= 0;
        return _sellMode.Value switch
        {
            1 => isScroll,
            2 => !isGold,
            3 => true,
            _ => false
        };
    }
}

public class ArcaneCrystalTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/ArcaneCrystal";

    private const string C = P.Menus + "/ArcaneCrystal";
    private MelonPreferences_Entry<int> _keep;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _keep = c.CreateEntry("keep_pickaxes", 0, "Pickaxes To Keep",
            "Hits the guild crystal with pickaxes, leaving this many unspent.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(1);
        yield return Ui.OpenGuildBuilding("arcaneCrystal");
        var hits = 0;
        while (hits < 80 && Ui.NumberAt(C + "/counters/currencyInteraction (Pickaxe)/quantity") > _keep.Value &&
               Ui.Clickable(C + "/crystalParent/hitButton"))
        {
            var pickaxesBefore = Ui.NumberAt(C + "/counters/currencyInteraction (Pickaxe)/quantity");
            var hit = false;
            yield return Ui.ClickUntil(Ui.Find(C + "/crystalParent/hitButton"),
                () => Ui.NumberAt(C + "/counters/currencyInteraction (Pickaxe)/quantity") < pickaxesBefore,
                value => hit = value, "arcane crystal hit", 1f);
            if (!hit)
            {
                Logger.Warning("[Arcane Crystal] Hit did not consume a pickaxe; stopping.");
                break;
            }
            yield return Ui.CloseRewards();
            hits++;
        }

        if (hits >= 80) NextRunTime = DateTime.Now.AddMinutes(2);
        yield return Ui.Click(C + "/closeButton");
    }
}

public class ChaosRiftTask : BotTask
{
    private const string R = P.Menus + "/ChaosRift";
    private const string UI = R + "/UIElements";
    private const string Hit = UI + "/godParent/hitButton";
    private const string Moon = UI + "/counters/currencyInteraction (MoonStone)/quantity";
    private const string Eclipse = UI + "/counters/currencyInteraction (EclipseStone)/quantity";
    private const string Shop = P.Menus + "/ChaosRiftShop/bg";
    private const string Items = Shop + "/submenus/supplies/items";
    private const string Runes = Shop + "/counters/currencyInteraction (DarkRune)/quantity";
    private const string MQ = P.Menus + "/MagicQuarters";
    private MelonPreferences_Entry<bool> _buyStones;
    private MelonPreferences_Entry<bool> _buyTomes;
    private MelonPreferences_Entry<bool> _holyUpgrades;
    private MelonPreferences_Entry<string> _holyGuardians;
    private MelonPreferences_Entry<string> _holyPriorityMode;
    private int _hits;

    protected override string NotificationPath => P.Notifications + "/ChaosRift";
    protected override bool UseScheduledTime => true;
    private static readonly string[] NotificationNames =
        { "ChaosRift", "ChaosRiftSupplies", "GuardianHolyUpgrade" };

    internal override IEnumerable<string> HandledNotificationNames => NotificationNames;

    protected override bool IsNotificationVisibleCore()
        => IsEnabled && DateTime.Now >= BubbleQuietUntil &&
           NotificationNames.Any(name => Ui.Visible(P.Notifications + "/" + name));

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _buyStones = c.CreateEntry("buy_eclipse_stones", true, "Buy Eclipse Stones",
            "Spends dark runes on eclipse stones in the rift shop, then hits with them.");
        _buyTomes = c.CreateEntry("buy_tomes", true, "Buy Tomes Of Power",
            "Spends dark runes on the current god's Tome of Power.");
        _holyUpgrades = c.CreateEntry("holy_damage_upgrades", true, "Holy Damage Upgrades",
            "Spends orbs of light on the holy damage upgrade (resets monthly).");
        _holyGuardians = c.CreateEntry("holy_guardians", "Vermilion,Grace,Ankaa,Azhar", "Holy Upgrade Guardians",
            "Guardians to upgrade, in priority order. Pick none to skip them all.");
        _holyPriorityMode = c.CreateEntry("holy_priority_mode", "balanced", "Holy Upgrade Strategy",
            "Balance checks every selected guardian in order. Focus first saves orbs for the first unlocked guardian.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(1);
        _hits = 0;
        if (Ui.Visible(P.Notifications + "/ChaosRift"))
            yield return Ui.Click(P.Notifications + "/ChaosRift", 2f);
        else
            yield return Ui.OpenGuildBuilding("chaosRift");
        if (!Ui.Visible(R))
        {
            Logger.Warning("[Chaos Rift] The Chaos Rift screen did not open; trying the holy upgrade bubble instead.");
            if (_holyUpgrades.Value && Ui.Visible(P.Notifications + "/GuardianHolyUpgrade"))
            {
                yield return Ui.Click(P.Notifications + "/GuardianHolyUpgrade", 2f);
                yield return HolyUpgrades();
            }
            yield break;
        }

        yield return HitAll();

        if (_buyStones.Value || _buyTomes.Value)
        {
            yield return Ui.Click(UI + "/actionButtons/shop", 2f);
            if (Ui.Visible(Shop))
            {
                yield return Ui.Click(Shop + "/submenuButtons/supplies/button", 1.2f);
                if (_buyStones.Value) yield return Buy("moonstoneBulk");
                if (_buyTomes.Value) yield return Buy("tomeOfPower");
                if (_buyStones.Value) yield return Buy("moonstoneOne");
                yield return Ui.Click(P.Menus + "/ChaosRiftShop/closeButton", 1.5f);
                yield return HitAll();
            }
        }

        if (_holyUpgrades.Value)
        {
            yield return Ui.Click(UI + "/actionButtons/upgrades", 2f);
            yield return HolyUpgrades();
        }
        if (_hits >= 30) NextRunTime = DateTime.Now.AddMinutes(2);
        yield return Ui.Click(R + "/closeButton");
    }

    private IEnumerator HolyUpgrades()
    {
        if (!Ui.Visible(MQ))
        {
            Logger.Warning("[Chaos Rift] Magic Quarters did not open; holy upgrades skipped.");
            yield break;
        }

        if (!UncappedUpgradePolicy.TryParse(_holyPriorityMode.Value, out var mode))
        {
            Logger.Warning("[Chaos Rift] Invalid holy upgrade strategy; no holy upgrades were attempted.");
            yield return Ui.Click(MQ + "/closeButton", 1.5f);
            yield break;
        }

        yield return HolyUpgradeBuyer.Run(_holyGuardians.Value, mode,
            () => NextRunTime = DateTime.Now.AddMinutes(2));
        yield return Ui.Click(MQ + "/closeButton", 1.5f);
    }
    private IEnumerator HitAll()
    {
        // Moon stones go first; the game switches the same button to eclipse stones when they run out.
        while (_hits < 30 && (Ui.NumberAt(Moon) > 0 || Ui.NumberAt(Eclipse) > 0) && Ui.Clickable(Hit))
        {
            var moonBefore = Ui.NumberAt(Moon);
            var eclipseBefore = Ui.NumberAt(Eclipse);
            yield return Ui.Click(Hit, 3f, "Chaos Rift hit");
            var moonAfter = Ui.NumberAt(Moon);
            var eclipseAfter = Ui.NumberAt(Eclipse);
            var spentStone = (moonBefore >= 0 && moonAfter >= 0 && moonAfter < moonBefore) ||
                             (eclipseBefore >= 0 && eclipseAfter >= 0 && eclipseAfter < eclipseBefore);
            if (!spentStone)
            {
                Logger.Warning("[Chaos Rift] No Moonstone or Eclipse Stone decrease was observed; stopping hits.");
                break;
            }

            yield return Ui.CloseRewards();
            _hits++;
        }
    }

    private IEnumerator Buy(string item)
    {
        var button = Items + $"/{item}/claimBg/purchaseButton";
        var isStone = item.StartsWith("moonstone");
        var currency = isStone ? Shop + "/counters/currencyInteraction (ExpeditionToken)/quantity" : Runes;
        var purchased = 0;
        for (var n = 0; n < 5; n++)
        {
            var cost = Ui.NumberAt(button + "/priceHolder/purchaseCost");
            var have = Ui.NumberAt(currency);
            if (cost <= 0)
            {
                Logger.Warning($"[Chaos Rift] Could not read the {item} cost; not buying.");
                break;
            }

            if (have < cost)
            {
                Logger.Info($"[Chaos Rift] {item} costs {cost}; have {have} of its required currency. Skipping.");
                break;
            }

            if (!Ui.Clickable(button)) break;
            var confirmed = false;
            yield return Ui.ClickUntil(Ui.Find(button), () => Ui.NumberAt(currency) < have,
                value => confirmed = value, "Chaos Rift " + item, 1.5f, 3f);
            if (!confirmed)
            {
                Logger.Warning($"[Chaos Rift] {item} currency did not decrease; stopping purchases.");
                break;
            }

            purchased++;
            if (Ui.MissingCurrency) yield break;
            yield return Ui.CloseRewards();
        }

        Logger.Info($"[Chaos Rift] Confirmed {purchased} purchase(s) of {item}.");
    }
}

public class AwakenHeroesTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/Awakening";

    private const string A = P.Menus + "/Awakening";
    private const string Button = A + "/awakeningOptions/awakenBg/awakenButton";

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.OpenGuildBuilding("awakening");
        if (!Ui.Visible(A))
        {
            Logger.Warning("[Awakening] The Awakening screen did not open; nothing attempted.");
            yield break;
        }

        var awakened = 0;
        for (var i = 0; i < 10; i++)
        {
            var have = Ui.NumberAt(A + "/counters/currencyInteraction (ArcaneCrystal)/quantity");
            var cost = Ui.NumberAt(Button + "/costText");
            if (cost <= 0)
            {
                Logger.Warning("[Awakening] Could not read the awakening cost; stopping.");
                break;
            }

            if (have < cost)
            {
                Logger.Info($"[Awakening] Need {cost} Arcane Crystals; have {have}. No awakening attempted; bubble left alone for 1 hour.");
                BubbleQuietUntil = DateTime.Now.AddHours(1);
                break;
            }

            var waited = 0f;
            while (!Ui.Clickable(Button) && waited < 5f)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }

            if (!Ui.Clickable(Button))
            {
                Logger.Warning($"[Awakening] Cost {cost}, crystals {have}, but the Awaken button is not clickable (visible: {Ui.Visible(Button)}); stopping.");
                break;
            }
            var confirmed = false;
            yield return Ui.ClickUntil(Ui.Find(Button),
                () => Ui.NumberAt(A + "/counters/currencyInteraction (ArcaneCrystal)/quantity") < have,
                value => confirmed = value, "hero awakening", 3f, 3f);
            if (!confirmed)
            {
                Logger.Warning("[Awakening] Arcane Crystal balance did not decrease; stopping to avoid retrying.");
                break;
            }

            awakened++;
            yield return Ui.CloseRewards();
        }

        Logger.Info($"[Awakening] Confirmed {awakened} awakening(s); crystals now {Ui.NumberAt(A + "/counters/currencyInteraction (ArcaneCrystal)/quantity"):0}.");
        yield return Ui.Click(A + "/closeButton");
    }
}

public class WarMachineUpgradeTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/Warmachines";
    protected override bool UseScheduledTime => true;

    private const string W = P.Menus + "/WarMachines";
    private const string Grid = W + "/warMachinesScrollView/Viewport/grid";
    private const string Name = W + "/bg/warMachinePreview/standardLevelProgress/bg/name";
    private const string Level = W + "/bg/warMachinePreview/standardLevelProgress/levelBg/level";
    private const string Tokens = W + "/counters/currencyInteraction (ExpeditionToken)/quantity";
    private const string Workshop = W + "/submenus/bg/workshopSubmenu";
    private const string Blueprints = W + "/submenus/bg/blueprintUpgradesSubmenu/upgrades";
    private MelonPreferences_Entry<string> _machine;
    private MelonPreferences_Entry<bool> _levels;
    private MelonPreferences_Entry<string> _blueprints;
    private MelonPreferences_Entry<string> _blueprintPriority;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _machine = c.CreateEntry("war_machine", "squad", "War Machines",
            "Which war machines to upgrade. \"Active squad\" means whatever machines are in the squad you use right " +
            "now, so it follows your squad when it changes. Squad machines go first, then the others.");
        // Older versions stored one machine by name ("Cloudfist"); the panel now stores the game's machine numbers.
        var old = _machine.Value ?? "";
        if (old.Length > 0 && old.Split(',').Any(x => x.Trim().Length > 0 && x.Trim() != "squad" && !x.Trim().All(char.IsDigit)))
        {
            string[] names = { "goliath", "fortress", "thunderclap", "firecracker", "aegis", "judgement", "curator", "sentinel",
                "harvester", "talos", "earthshatter", "cloudfist", "hunter" };
            var converted = old.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x =>
            {
                var n = Norm(x) == "earthshatterer" ? "earthshatter" : Norm(x);
                var i = Array.IndexOf(names, n);
                return i >= 0 ? i.ToString() : x;
            });
            _machine.Value = string.Join(",", converted);
        }
        _levels = c.CreateEntry("upgrade_levels", true, "Upgrade Levels",
            "Levels them up in the workshop with expedition tokens and components.");
        _blueprints = c.CreateEntry("blueprints", "damage", "Blueprints",
            "Blueprint upgrades: none, damage, health, armor, or all.");
        _blueprintPriority = c.CreateEntry("blueprint_priority", "damage,health,armor", "Blueprint Priority",
            "When Blueprints is set to all, fill the selected blueprint types in this order.");
    }

    /// <summary>The machines to work on, as the game's machine numbers, in the order chosen.</summary>
    private List<int> Targets()
    {
        var owned = new Dictionary<int, WarMachine>();
        var list = Il2Cpp.GameInitialize.HandlerLoader?.warMachineHandler?.warMachineData?.warMachineList;
        for (var i = 0; list != null && i < list.Count; i++)
            if (list[i] != null) owned[(int)list[i].machineCode] = list[i];

        var result = new List<int>();
        foreach (var raw in (_machine.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pick = raw.Trim();
            if (pick.Equals("squad", StringComparison.OrdinalIgnoreCase))
            {
                var squad = Il2Cpp.GameInitialize.HandlerLoader?.warMachineHandler?.warMachineData?.formationList;
                for (var i = 0; squad != null && i < squad.Count; i++)
                    foreach (var kv in owned)
                        if (squad[i] != null && (kv.Value.code == squad[i].code || kv.Value.machineCode.ToString() == squad[i].code) &&
                            !result.Contains(kv.Key))
                            result.Add(kv.Key);
                continue;
            }
            // Older settings stored names; numbers and names both work.
            foreach (var kv in owned)
                if ((pick == kv.Key.ToString() || Norm(pick) == Norm(kv.Value.machineCode.ToString()) ||
                     (Norm(pick) == "earthshatterer" && kv.Value.machineCode.ToString() == "Earthshatter")) && !result.Contains(kv.Key))
                    result.Add(kv.Key);
        }
        return result;
    }

    private static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(1);
        var targets = Targets();
        if (targets.Count == 0)
        {
            Logger.Info("[War Machine] No war machine picked (or none of the picked ones is unlocked yet).");
            yield break;
        }

        yield return Ui.OpenTownBuilding("engineer");
        yield return Ui.Click(P.Popups + "/GarageSelection/bg/garage", 2f);
        if (!Ui.Visible(W)) yield break;

        // The currency counters only exist on a machine's own screen, not on the garage list, so nothing is
        // read here: each machine is opened and judged on its own screen.
        _spent = 0;
        foreach (var number in targets)
        {
            var square = FindSquare(number);
            if (square == null)
            {
                Logger.Warning($"[War Machine] Machine {number} is not in the garage list; lower tiers were left untouched.");
                NextRunTime = DateTime.Now.AddMinutes(2);
                BubbleQuietUntil = NextRunTime;
                break;
            }
            var selected = false;
            yield return Ui.ClickUntil(square,
                () => square.Find("highlighted") != null &&
                      square.Find("highlighted").gameObject.activeInHierarchy &&
                      !string.IsNullOrWhiteSpace(Ui.Text(Name)),
                value => selected = value, $"select machine {number}", 1f);
            if (!selected)
            {
                Logger.Warning($"[War Machine] Selection of machine {number} was not confirmed; lower tiers were left untouched.");
                NextRunTime = DateTime.Now.AddMinutes(2);
                BubbleQuietUntil = NextRunTime;
                break;
            }
            var action = PriorityTierAction.Advance;
            yield return UpgradeSelected(value => action = value);
            if (action == PriorityTierAction.Advance) continue;
            NextRunTime = DateTime.Now.AddMinutes(action == PriorityTierAction.RetrySoon ? 2 : 60);
            BubbleQuietUntil = NextRunTime;
            Logger.Info($"[War Machine] Holding at machine {number}; lower tiers stay untouched.");
            break;
        }

        if (_spent == 0)
        {
            Logger.Info("[War Machine] Opened the machine screens but nothing was affordable; bubble left alone for 1 hour.");
            BubbleQuietUntil = DateTime.Now.AddHours(1);
        }

        yield return Ui.Click(W + "/closeButton");
    }

    private int _spent;

    /// <summary>The garage tile of a machine, found by the machine it holds rather than its label.</summary>
    private static Transform FindSquare(int number)
    {
        foreach (var t in Ui.VisibleChildren(Grid).Where(s => s.name.StartsWith("warMachineSquare")).ToList())
        {
            var sq = t.GetComponent<WarMachineInteractionSquare>();
            if (sq?.warMachine != null && (int)sq.warMachine.machineCode == number) return t;
        }
        return null;
    }

    private IEnumerator UpgradeSelected(Action<PriorityTierAction> result)
    {
        var name = Ui.Text(Name);
        var levelOutcome = PriorityTierAction.Advance;
        if (_levels.Value)
        {
            yield return Ui.Click(W + "/submenus/submenuButtons/workshop", 1.2f);
            var ups = 0;
            if (!Ui.Visible(Workshop))
            {
                Logger.Warning($"[War Machine] {name}: workshop did not open.");
                levelOutcome = PriorityTierAction.Wait;
            }
            else
                for (var i = 0; i < 20; i++)
                {
                    var button = Workshop + "/upgradeButton";
                    if (!Ui.Visible(button)) break;
                    var tokens = Ui.NumberAt(Tokens);
                    var cost = Ui.NumberAt(button + "/costText");
                    var componentsOk = Enumerable.Range(0, 3).All(n =>
                    {
                        var v = Ui.Text(Workshop + $"/wmComponentProgress ({n})/value");
                        if (string.IsNullOrEmpty(v)) return true;
                        var parts = v.Split('/');
                        if (parts.Length != 2) return false;
                        var have = Ui.Parse(parts[0]);
                        var required = Ui.Parse(parts[1]);
                        return have >= 0 && required >= 0 && have >= required;
                    });
                    if (cost <= 0 || tokens < 0 || tokens < cost || !componentsOk || !Ui.Clickable(button))
                    {
                        levelOutcome = PriorityTierAction.Wait;
                        break;
                    }
                    var levelBefore = Ui.Text(Level);
                    var tokensBefore = tokens;
                    var changed = false;
                    yield return Ui.ClickUntil(Ui.Find(button),
                        () => PurchaseObservation.LevelIncreased(levelBefore, Ui.Text(Level)) ||
                              PurchaseObservation.Decreased(tokensBefore, Ui.NumberAt(Tokens)),
                        value => changed = value, $"{name} level upgrade", 1.5f);
                    if (!changed)
                    {
                        Logger.Warning($"[War Machine] {name}: stopping level upgrades after no verified level or token change.");
                        levelOutcome = PriorityTierAction.Wait;
                        break;
                    }

                    ups++;
                }
            _spent += ups;
            if (ups > 0) Logger.Info($"[War Machine] {name}: +{ups} level(s).");
            if (ups == 20 && Ui.Visible(Workshop + "/upgradeButton"))
            {
                var cost = Ui.NumberAt(Workshop + "/upgradeButton/costText");
                var tokens = Ui.NumberAt(Tokens);
                levelOutcome = cost > 0 && tokens >= cost && Ui.Clickable(Workshop + "/upgradeButton")
                    ? PriorityTierAction.RetrySoon : PriorityTierAction.Wait;
            }
        }

        if (!WarMachineBlueprintPolicy.TryGetSlots(_blueprints.Value, _blueprintPriority.Value, out var slots))
        {
            Logger.Warning($"[War Machine] {name}: blueprint selection or priority is invalid.");
            result(WarMachineBlueprintPolicy.Combine(levelOutcome, PriorityTierAction.Wait));
            yield break;
        }
        if (slots.Length == 0) { result(levelOutcome); yield break; }

        yield return Ui.Click(W + "/submenus/submenuButtons/blueprintUpgrades", 1.2f);
        if (!Ui.Visible(W + "/submenus/bg/blueprintUpgradesSubmenu"))
        {
            Logger.Warning($"[War Machine] {name}: blueprint upgrades did not open.");
            result(WarMachineBlueprintPolicy.Combine(levelOutcome, PriorityTierAction.Wait));
            yield break;
        }
        var blueprintCurrency = Ui.VisibleChildren(W + "/counters")
            .FirstOrDefault(t => t.name.IndexOf("Blueprint", StringComparison.OrdinalIgnoreCase) >= 0);
        if (blueprintCurrency == null || Ui.NumberOf(blueprintCurrency.Find("quantity")) < 0)
        {
            Logger.Warning($"[War Machine] {name}: blueprint count is unreadable.");
            result(WarMachineBlueprintPolicy.Combine(levelOutcome, PriorityTierAction.Wait));
            yield break;
        }

        var blueprintOutcome = PriorityTierAction.Advance;
        var bought = 0;
        foreach (var s in slots)
        {
            var button = Blueprints + $"/wmBlueprintUpgrade ({s})/upgradeButton";
            var level = Blueprints + $"/wmBlueprintUpgrade ({s})/level";
            var purchases = 0;
            for (; purchases < 20; purchases++)
            {
                if (!Ui.Visible(button)) break;
                var have = Ui.NumberOf(blueprintCurrency.Find("quantity"));
                var cost = Ui.NumberAt(button + "/costText");
                if (cost <= 0 || have < 0 || have < cost || !Ui.Clickable(button))
                {
                    blueprintOutcome = PriorityTierAction.Wait;
                    break;
                }
                var before = have;
                var levelBefore = Ui.Text(level);
                var changed = false;
                yield return Ui.ClickUntil(Ui.Find(button),
                    () => PurchaseObservation.Decreased(before, Ui.NumberOf(blueprintCurrency.Find("quantity"))) ||
                          PurchaseObservation.LevelIncreased(levelBefore, Ui.Text(level)),
                    value => changed = value, $"{name} blueprint {s}", 1.2f);
                if (!changed)
                {
                    Logger.Warning($"[War Machine] {name}: stopping blueprint upgrades after no verified currency change.");
                    blueprintOutcome = PriorityTierAction.Wait;
                    break;
                }

                bought++;
            }
            if (purchases == 20 && Ui.Visible(button))
            {
                var have = Ui.NumberOf(blueprintCurrency.Find("quantity"));
                var cost = Ui.NumberAt(button + "/costText");
                blueprintOutcome = cost > 0 && have >= cost && Ui.Clickable(button)
                    ? PriorityTierAction.RetrySoon : PriorityTierAction.Wait;
            }
            if (blueprintOutcome != PriorityTierAction.Advance) break;
        }
        _spent += bought;
        if (bought > 0) Logger.Info($"[War Machine] {name}: {bought} blueprint upgrade(s).");
        result(WarMachineBlueprintPolicy.Combine(levelOutcome, blueprintOutcome));
    }
}

public class EventChallengesTask : BotTask
{
    private const string List = P.Events + "/EventManager/bg/verticalLayout/Scroll View/Viewport/Content/activeEvents";
    private const string Mini = P.Events + "/MiniEvents/bg";
    private const string Challenges = Mini + "/submenus/challenges/bg/challengesLayout";

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(2);
        yield return Ui.Click(P.EventsButton, 2f);
        var count = Ui.VisibleChildren(List).Count(t => t.name.StartsWith("eventInteraction"));
        var confirmed = 0;
        var unconfirmed = 0;
        for (var i = 0; i < count; i++)
        {
            if (!Ui.Visible(List))
            {
                yield return Ui.Click(P.EventsButton, 2f);
            }

            var entry = Ui.VisibleChildren(List).Where(t => t.name.StartsWith("eventInteraction")).Skip(i).FirstOrDefault();
            if (entry == null) break;
            yield return Ui.Click(entry, 2f);
            if (!Ui.Visible(Mini)) continue;

            yield return Ui.Click(Mini + "/submenuButtons/challenges", 1.5f);
            foreach (var ch in Ui.VisibleChildren(Challenges).ToList())
            {
                var claim = ch.Find("unlocked/reward/claimButton");
                if (!Ui.Clickable(claim)) continue;
                var rewardWasVisible = Ui.Visible(P.RewardsClose);
                var claimChanged = false;
                yield return Ui.ClickUntil(claim,
                    () => !Ui.Clickable(claim) ||
                          (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                    value => claimChanged = value, "event challenge reward", 1.5f);
                if (claimChanged) confirmed++;
                else unconfirmed++;
                yield return Ui.CloseRewards();
            }

            yield return Ui.Click(Mini + "/closeButton", 1.5f);
        }

        yield return Ui.Click(P.Events + "/EventManager/bg/closeButton");
        Logger.Info($"[Events] Claim controls changed for {confirmed} reward(s); " +
                    $"{unconfirmed} claim(s) unconfirmed. Reward receipt is not independently verified.");
    }
}

public class LiberationMissionsTask : BotTask
{
    private const string Map = P.Menus + "/WorldMap";
    private const string List = P.Popups + "/WFLiberationMissions";
    private const string Grid = List + "/bg/missionsScrollView/Viewport/missionGrid";

    public override int MinimumLevel => LevelRequirements.WarfrontCampaign;

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(4);
        yield return Ui.Click(P.MapButton, 2f);
        yield return Ui.Click(Map + "/submenuButtons/warfrontCampaignButton", 2f);
        yield return Ui.Click(Map + "/submenus/warfrontCampaignSubmenu/dailyMissionsButton", 2f);
        yield return Ui.Click(P.Popups + "/WFDailyMissions/bg/liberationMissions/openButton", 2f);

        // Fight one area after another until none is left, a fight is lost, or time runs short.
        var started = Time.unscaledTime;
        while (Time.unscaledTime - started < 110f)
        {
            if (!Ui.Visible(Grid)) break;
            var fight = Ui.VisibleChildren(Grid).Select(m => m.Find("fightButton")).FirstOrDefault(Ui.Clickable);
            if (fight == null) break;

            yield return Ui.Click(fight, 1.5f, "liberation fight");
            bool? won = null;
            yield return Ui.FinishBattle(r => won = r);

            if (won == false)
            {
                Logger.Info("[Liberation] Lost a fight; trying again on a later cycle.");
                break;
            }

            if (won == null)
            {
                NextRunTime = DateTime.Now.AddMinutes(2);
                yield break;
            }

            NextRunTime = DateTime.Now.AddMinutes(1); // more areas may be left when time runs out
            yield return new WaitForSeconds(1.5f);
        }
    }
}

public class OracleBlessingsTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/OracleBlessings";
    protected override bool UseScheduledTime => true;

    private const string O = P.Menus + "/Oracle";
    private const string List = O + "/submenus/bg/blessingsSubmenu/blessings";
    private const string Preview = P.Popups + "/OracleBlessingPreview/bg";
    private const string Bless = Preview + "/darkBlueFrame/buttonRect/buyUpgradeButton";
    private MelonPreferences_Entry<string> _priority;

    public override int MinimumLevel => LevelRequirements.Oracle;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority",
            "Fate,Prestigious,Raining gold,Damage Specialization,Guardian Power,Mana Heroes,Energy Heroes," +
            "Rage Heroes,Melee Heroes,Ranged Heroes,Spellcaster Heroes,Tank Specialization,Healer Specialization",
            "Priority", "Blessings to fill in order. Change the list to suit your party; unlisted blessings follow.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.OpenTownBuilding("oracle");
        yield return Ui.Click(O + "/submenuButtons/blessings", 1.5f);
        if (!Ui.Visible(List)) yield break;

        var tiles = Ui.VisibleChildren(List).Where(b => b.name.StartsWith("blessing")).ToList();
        var known = new List<(Transform tile, string name)>();
        const string scope = "oracle-blessings";
        foreach (var blessing in tiles)
        {
            if (UpgradeNames.TryGet(blessing, scope, out var remembered))
            {
                known.Add((blessing, remembered));
                continue;
            }
            if (!Ui.Clickable(blessing)) continue;
            if (Ui.Visible(Preview))
            {
                Logger.Warning("[Oracle] A previous preview is still open; lower tiers were left untouched.");
                UpgradeNames.Flush();
                yield break;
            }
            yield return Ui.Click(blessing, 1.2f);
            var name = Ui.Text(Preview + "/darkBlueFrame/upgradeName").Trim();
            if (!Ui.Visible(Preview) || string.IsNullOrWhiteSpace(name))
            {
                Logger.Warning("[Oracle] A blessing preview or name was unreadable; lower tiers were left untouched.");
                UpgradeNames.Flush();
                if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.8f);
                yield return Ui.Click(O + "/closeButton");
                yield break;
            }
            UpgradeNames.Remember(blessing, scope, name);
            known.Add((blessing, name));
            yield return Ui.Click(Preview + "/closeButton", 0.8f);
        }
        UpgradeNames.Flush();

        var order = (_priority.Value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        foreach (var blessing in known.OrderBy(item => PriorityTier.Rank(order, item.name)))
        {
            if (!Ui.Clickable(blessing.tile)) continue;
            if (Ui.Visible(Preview))
            {
                Logger.Warning("[Oracle] A previous preview is still open; lower tiers were left untouched.");
                break;
            }
            yield return Ui.Click(blessing.tile, 1.2f);
            if (!Ui.Visible(Preview))
            {
                Logger.Warning($"[Oracle] {blessing.name}: preview did not open; lower tiers were left untouched.");
                break;
            }
            var actualName = Ui.Text(Preview + "/darkBlueFrame/upgradeName");
            if (!UpgradeNames.Matches(blessing.tile, scope, blessing.name, actualName))
            {
                UpgradeNames.Flush();
                Logger.Warning($"[Oracle] Expected {blessing.name}, found '{actualName}'; reordering before buying.");
                if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.8f);
                NextRunTime = DateTime.Now.AddMinutes(2);
                BubbleQuietUntil = NextRunTime;
                break;
            }
            var levelPath = Preview + "/darkBlueFrame/upgradeLevelText";
            var count = 0;
            var failed = false;
            while (count < 10 && Ui.Clickable(Bless))
            {
                var levelBefore = Ui.Text(levelPath);
                if (string.IsNullOrWhiteSpace(levelBefore))
                {
                    Logger.Warning($"[Oracle] {blessing.name}: level unreadable; no purchase attempted.");
                    failed = true;
                    break;
                }
                var changed = false;
                yield return Ui.ClickUntil(Ui.Find(Bless),
                    () => PurchaseObservation.LevelIncreased(levelBefore, Ui.Text(levelPath)),
                    value => changed = value, $"Oracle blessing: {blessing.name}", 1.5f);
                if (!changed)
                {
                    Logger.Warning($"[Oracle] Stopping {blessing.name}: blessing level did not increase.");
                    failed = true;
                    break;
                }
                count++;
            }

            if (!Ui.Visible(Preview))
            {
                Logger.Warning($"[Oracle] {blessing.name}: preview closed unexpectedly; lower tiers were left untouched.");
                break;
            }
            var finalLevel = Ui.Text(levelPath);
            var maxed = NodeBuyer.IsMaxed(finalLevel);
            var available = Ui.Visible(Bless);
            var missingControl = Ui.Find(Bless) == null;
            var stillClickable = Ui.Clickable(Bless);
            yield return Ui.Click(Preview + "/closeButton", 0.8f);
            if (failed || string.IsNullOrWhiteSpace(finalLevel) || (!maxed && (available || missingControl)))
            {
                NextRunTime = DateTime.Now.AddMinutes(count >= 10 && stillClickable ? 2 : 60);
                BubbleQuietUntil = NextRunTime;
                Logger.Info($"[Oracle] Holding at {blessing.name}; lower tiers stay untouched.");
                break;
            }
        }

        yield return Ui.Click(O + "/closeButton");
    }
}

public class PersonalTreeTask : BotTask
{
    private const string T = P.Menus + "/TreeOfLife";
    private const string Nodes = T + "/submenus/personalTree/talentTree";
    private const string Tokens = T + "/submenus/personalTree/counters/currencyInteraction (ExpeditionToken)/quantity";
    private const string Preview = P.Popups + "/TOLPersonalUpgradePreview/bg";
    private const string Buy = Preview + "/normal/buyUpgradeButton";
    private MelonPreferences_Entry<string> _priority;
    private MelonPreferences_Entry<int> _keepTokens;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority", "Raining gold,Battle cry,Prestigious,All main attributes,Attribute damage," +
                                              "Firestone effect,Hero level up cost,Attribute health,Attribute armor,Guardian," +
                                              "Precision,Fist fight,Magic spells", "Priority",
            "Upgrade names in the order to buy, comma-separated. Upgrades not listed are bought last.");
        _keepTokens = c.CreateEntry("keep_tokens", 0, "Tokens To Keep",
            "Expedition tokens to leave unspent (war machine levels use them too).");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.OpenGuildBuilding("treeOfLife");
        yield return Ui.Click(T + "/submenuButtons/personalTree", 1.5f);
        if (!Ui.Visible(Nodes)) yield break;
        if (Ui.NumberAt(Tokens) <= _keepTokens.Value)
        {
            Logger.Info($"[Personal Tree] Skipping upgrade previews: tokens {Ui.NumberAt(Tokens)}, reserve {_keepTokens.Value}.");
            yield return Ui.Click(T + "/closeButton");
            yield break;
        }

        var info = new System.Collections.Generic.List<(Transform node, string name, double cost, bool maxed)>();
        var skipped = 0;
        const string scope = "personal-tree";
        foreach (var node in Ui.VisibleChildren(Nodes).Where(n => n.name.StartsWith("treeOfLifePersonalUpgrade")).ToList())
        {
            if (NodeBuyer.CanSkip(node, Ui.NumberAt(Tokens) - _keepTokens.Value,
                    retryUnaffordableAfterCooldown: false, scope: scope))
            {
                if (NodeBuyer.TryGetKnown(node, out var knownMaxed, out var knownCost, out var knownName, scope) &&
                    !knownMaxed && knownCost > 0 && !string.IsNullOrEmpty(knownName))
                    info.Add((node, knownName, knownCost, false));
                skipped++;
                continue;
            }

            if (NodeBuyer.TryGetKnown(node, out var cachedMaxed, out var cachedCost, out var cachedName, scope) &&
                !cachedMaxed && cachedCost > 0 && !string.IsNullOrEmpty(cachedName))
            {
                info.Add((node, cachedName, cachedCost, false));
                continue;
            }

            if (UpgradeNames.TryGet(node, scope, out var rememberedName))
            {
                info.Add((node, rememberedName, 0, false));
                continue;
            }

            if (!Ui.Clickable(node)) continue;
            if (Ui.Visible(Preview))
            {
                Logger.Warning("[Personal Tree] A previous preview is still open; lower tiers were left untouched.");
                UpgradeNames.Flush();
                yield break;
            }
            yield return Ui.Click(node, 1f);
            if (!Ui.Visible(Preview))
            {
                Logger.Warning("[Personal Tree] An unknown upgrade preview did not open; lower tiers were left untouched.");
                UpgradeNames.Flush();
                if (Ui.Visible(T)) yield return Ui.Click(T + "/closeButton");
                yield break;
            }
            var name = Ui.Text(Preview + "/upgradeName").Trim();
            if (string.IsNullOrEmpty(name))
            {
                Logger.Warning("[Personal Tree] An upgrade name was unreadable; lower tiers were left untouched.");
                yield return Ui.Click(Preview + "/closeButton", 0.6f);
                UpgradeNames.Flush();
                yield return Ui.Click(T + "/closeButton");
                yield break;
            }
            var level = Ui.Text(Preview + "/upgradeLevelText");
            var maxed = NodeBuyer.IsMaxed(level);
            var cost = Ui.NumberAt(Buy + "/cost");
            var done = maxed || !Ui.Visible(Buy);
            NodeBuyer.Remember(node, maxed, done ? 0 : cost, name, scope);
            if (!done && cost <= 0)
            {
                Logger.Warning($"[Personal Tree] {name}: price unreadable; lower tiers were left untouched.");
                yield return Ui.Click(Preview + "/closeButton", 0.6f);
                UpgradeNames.Flush();
                yield return Ui.Click(T + "/closeButton");
                yield break;
            }
            info.Add((node, name, cost, done));
            yield return Ui.Click(Preview + "/closeButton", 0.6f);
        }
        UpgradeNames.Flush();

        Logger.Info($"[Personal Tree] Previewed {info.Count} upgrade(s), skipped {skipped} already known as unaffordable, maxed or locked.");

        var order = (_priority.Value ?? string.Empty).Split(',').Select(s => s.Trim().ToLowerInvariant()).ToList();
        foreach (var up in info.Where(u => !u.maxed && u.cost >= 0)
                     .OrderBy(u => PriorityTier.Rank(order, u.name)).ThenBy(u => u.cost).ToList())
        {
            if (!Ui.Clickable(up.node)) continue;
            if (Ui.Visible(Preview))
            {
                Logger.Warning("[Personal Tree] A previous preview is still open; lower tiers were left untouched.");
                break;
            }
            yield return Ui.Click(up.node, 1f);
            if (!Ui.Visible(Preview))
            {
                Logger.Warning($"[Personal Tree] {up.name}: preview did not open; lower tiers were left untouched.");
                break;
            }
            var actualName = Ui.Text(Preview + "/upgradeName");
            if (!UpgradeNames.Matches(up.node, scope, up.name, actualName))
            {
                NodeBuyer.Forget(up.node, scope);
                UpgradeNames.Flush();
                Logger.Warning($"[Personal Tree] Expected {up.name}, found '{actualName}'; reordering before buying.");
                if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.6f);
                NextRunTime = DateTime.Now.AddMinutes(2);
                break;
            }
            var purchases = 0;
            for (; purchases < 5; purchases++)
            {
                var cost = Ui.NumberAt(Buy + "/cost");
                if (cost <= 0 || Ui.NumberAt(Tokens) - _keepTokens.Value < cost || !Ui.Clickable(Buy)) break;
                var levelBefore = Ui.Text(Preview + "/upgradeLevelText");
                var tokensBefore = Ui.NumberAt(Tokens);
                var changed = false;
                yield return Ui.ClickUntil(Ui.Find(Buy),
                    () => PurchaseObservation.LevelIncreased(levelBefore, Ui.Text(Preview + "/upgradeLevelText")) ||
                          PurchaseObservation.ChangedPrice(cost, Ui.NumberAt(Buy + "/cost")) ||
                          PurchaseObservation.Decreased(tokensBefore, Ui.NumberAt(Tokens)),
                    value => changed = value, up.name, 1.5f);
                if (!changed)
                {
                    Logger.Warning($"[Personal Tree] Stopping after {up.name} made no verified progress.");
                    if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.6f);
                    yield return Ui.Click(T + "/closeButton");
                    yield break;
                }
            }

            if (!Ui.Visible(Preview))
            {
                Logger.Warning($"[Personal Tree] {up.name}: preview closed unexpectedly; lower tiers were left untouched.");
                break;
            }
            var finalLevel = Ui.Text(Preview + "/upgradeLevelText");
            var finalMaxed = NodeBuyer.IsMaxed(finalLevel);
            var finalCost = Ui.NumberAt(Buy + "/cost");
            var available = Ui.Visible(Buy);
            var missingCurrency = Ui.MissingCurrency;
            var action = !finalMaxed && Ui.Find(Buy) == null
                ? PriorityTierAction.Wait
                : PriorityTier.AfterPass(finalMaxed, available, purchases >= 5,
                    Ui.Clickable(Buy), finalCost, Ui.NumberAt(Tokens) - _keepTokens.Value);
            NodeBuyer.Remember(up.node, finalMaxed, finalMaxed || !available ? 0 : finalCost, up.name, scope);
            yield return Ui.Click(Preview + "/closeButton", 0.6f);
            if (missingCurrency || action != PriorityTierAction.Advance)
            {
                if (action == PriorityTierAction.RetrySoon) NextRunTime = DateTime.Now.AddMinutes(2);
                Logger.Info($"[Personal Tree] Holding at {up.name}; lower tiers stay untouched.");
                break;
            }
        }

        yield return Ui.Click(T + "/closeButton");
    }
}

public class QuestsTask : BotTask
{
    private const string C = P.Popups + "/Character/bg";
    private const string Q = C + "/submenus/quests/bg";

    protected override string NotificationPath => P.Notifications + "/Quests";

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(1);
        if (Ui.Visible(P.Notifications + "/Quests"))
            yield return Ui.Click(P.Notifications + "/Quests", 2f);
        else
            yield return Ui.Click(P.Battle + "/topLeftSideUI/playerAvatar", 2f);
        if (!Ui.Visible(C)) yield break;

        yield return Ui.Click(C + "/submenuButtons/quests", 1.5f);
        var confirmed = 0;
        var unconfirmed = 0;
        var stalled = false;
        foreach (var tab in new[] { "dailyButton", "weeklyButton" })
        {
            if (stalled) break;
            yield return Ui.Click(Q + "/submenuButtons/" + tab, 1.5f);
            foreach (var scroll in Ui.VisibleChildren(Q + "/submenus").ToList())
            {
                if (stalled) break;
                var grid = scroll.Find("Viewport/grid");
                if (grid == null) continue;
                for (var i = 0; i < grid.childCount; i++)
                {
                    var claim = grid.GetChild(i).Find("claimButton");
                    if (!Ui.Clickable(claim)) continue;
                    var rewardWasVisible = Ui.Visible(P.RewardsClose);
                    var changed = false;
                    yield return Ui.ClickUntil(claim,
                        () => !Ui.Clickable(claim) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                        value => changed = value, "quest reward", 1.5f);
                    yield return Ui.CloseRewards();
                    if (changed) confirmed++;
                    else
                    {
                        unconfirmed++;
                        stalled = true;
                        break;
                    }
                }
            }
        }

        Logger.Info($"[Quests] Claim controls changed for {confirmed} reward(s); {unconfirmed} claim(s) unconfirmed. " +
                    "Reward receipt is not independently verified.");
        yield return Ui.Click(C + "/closeButton");
    }
}
