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
        for (var i = 0; i < count && i < 30; i++)
        {
            var item = Ui.VisibleChildren(List).Skip(_deleteClaimed.Value ? 0 : i).FirstOrDefault();
            if (item == null) break;
            yield return Ui.Click(item, 1f);
            if (Ui.Clickable(Claim))
            {
                var rewardWasVisible = Ui.Visible(P.RewardsClose);
                var confirmed = false;
                yield return Ui.ClickUntil(Ui.Find(Claim),
                    () => !Ui.Clickable(Claim) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                    value => confirmed = value, "mail reward claim", 1.5f);
                yield return Ui.CloseRewards();
                if (confirmed)
                {
                    claimsWithStateChange++;
                    if (_deleteClaimed.Value) yield return Ui.Click(Delete, 1f);
                }
                else
                {
                    claimsUnconfirmed++;
                    if (_deleteClaimed.Value) break;
                }
            }
            else if (_deleteClaimed.Value) break;
        }

        Logger.Info($"[Mail] Claim control changed for {claimsWithStateChange} message(s); {claimsUnconfirmed} claim(s) unconfirmed. " +
                    "Reward receipt is not independently verified.");
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
    private const string UpgradeList = M + "/bg/upgradesSubmenu/upgradesScrollView/Viewport/Content/upgradesList";
    private const string NextTree = M + "/bg/upgradesSubmenu/navigation/goForthTree";
    private const string GearGrid = M + "/bg/emblemMarketSubmenu/categories/gearCategory/scrollView/Viewport/Content/productGrid";
    private MelonPreferences_Entry<int> _sellMode;
    private MelonPreferences_Entry<bool> _buyUpgrades;
    private MelonPreferences_Entry<bool> _buyChests;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _sellMode = c.CreateEntry("sell_mode", 2, "Sell Items",
            "0 = sell nothing, 1 = scrolls only, 2 = everything except gold items, 3 = everything.");
        _buyUpgrades = c.CreateEntry("buy_upgrades", true, "Buy Exotic Upgrades",
            "Spends exotic coins on every upgrade the game lets you afford.");
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
                        () => Ui.NumberOf(item.Find("itemBg/quantity")) < quantityBefore ||
                              Ui.NumberAt(ExoticCoins) > coinsBefore,
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
            if (Ui.NumberAt(ExoticCoins) <= 0)
            {
                Logger.Info("[Exotic Merchant] Skipping upgrade scans: no exotic coins available.");
            }
            else
            {
                yield return Ui.Click(M + "/submenuButtons/upgrades", 1.5f);
                for (var tree = 0; tree < 8; tree++)
                {
                    foreach (var up in Ui.VisibleChildren(UpgradeList).ToList())
                    {
                        var button = up.Find("upgradeButton");
                        while (clicks < 90 && Ui.Clickable(button))
                        {
                            var name = Ui.Text(up.Find("upgradeName"));
                            var currencyBefore = Ui.NumberAt(ExoticCoins);
                            var costBefore = Ui.NumberOf(button.Find("costText"));
                            var confirmed = false;
                            yield return Ui.ClickUntil(button,
                                () => Ui.NumberAt(ExoticCoins) < currencyBefore ||
                                      Ui.NumberOf(button.Find("costText")) != costBefore,
                                value => confirmed = value, name, 0.8f);
                            if (!confirmed)
                            {
                                Logger.Warning($"[Exotic Merchant] Stopping at {name}: no cost or currency change was confirmed.");
                                if (Ui.Visible(P.Popups + "/GenericMessage/bg"))
                                    yield return Ui.Click(P.Popups + "/GenericMessage/bg/closeButton");
                                yield return Ui.Click(P.Menus + "/ExoticMerchant/closeButton");
                                yield break;
                            }
                            clicks++;
                        }
                    }

                    if (!Ui.Clickable(NextTree)) break;
                    yield return Ui.Click(NextTree, 1.2f);
                    // A locked tree answers with a message instead of opening.
                    if (Ui.Visible(P.Popups + "/GenericMessage/bg"))
                    {
                        yield return Ui.Click(P.Popups + "/GenericMessage/bg/closeButton");
                        break;
                    }
                }
            }
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
    private const string Holy = MQ + "/submenus/bg/chaosRiftSubmenu/holyDamageUpgrade/bg/upgradeButton";
    private const string Orbs = MQ + "/counters/currencyInteraction (OrbOfLight)/quantity";
    private MelonPreferences_Entry<bool> _buyStones;
    private MelonPreferences_Entry<bool> _buyTomes;
    private MelonPreferences_Entry<bool> _holyUpgrades;
    private MelonPreferences_Entry<string> _holyGuardians;
    private int _hits;

    protected override string NotificationPath => P.Notifications + "/ChaosRift";
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
            "Which guardians get holy damage upgrades. Pick none to skip them all.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(1);
        _hits = 0;
        if (Ui.Visible(P.Notifications + "/ChaosRift"))
            yield return Ui.Click(P.Notifications + "/ChaosRift", 2f);
        else
            yield return Ui.OpenGuildBuilding("chaosRift");
        if (!Ui.Visible(R)) yield break;

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
            if (Ui.Visible(MQ))
            {
                var wanted = (_holyGuardians.Value ?? string.Empty).Split(',')
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                foreach (var g in Ui.VisibleChildren(MQ + "/guardianList").Where(Ui.Clickable).ToList())
                {
                    if (wanted.Count == 0) break;
                    yield return Ui.Click(g, 1.2f);
                    var name = Ui.Text(MQ + "/guardianPreview/info/guardianName");
                    if (!wanted.Any(w => w.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    yield return Ui.Click(MQ + "/submenus/submenuButtons/chaosRift", 1.2f);
                    for (var n = 0; n < 10; n++)
                    {
                        var cost = Ui.NumberAt(Holy + "/layout/costText");
                        var orbsBefore = Ui.NumberAt(Orbs);
                        if (cost <= 0 || orbsBefore < cost || !Ui.Clickable(Holy)) break;
                        var upgraded = false;
                        yield return Ui.ClickUntil(Ui.Find(Holy),
                            () => Ui.NumberAt(Orbs) < orbsBefore ||
                                  Ui.NumberAt(Holy + "/layout/costText") != cost,
                            value => upgraded = value, "holy damage upgrade", 1.2f, 2f);
                        if (!upgraded) break;
                    }
                }

                yield return Ui.Click(MQ + "/closeButton", 1.5f);
            }
        }

        if (_hits >= 30) NextRunTime = DateTime.Now.AddMinutes(2);
        yield return Ui.Click(R + "/closeButton");
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
                Logger.Debug($"[War Machine] Machine {number} is not in the garage list.");
                continue;
            }
            yield return Ui.Click(square, 1f);
            yield return UpgradeSelected();
        }

        // Machines outside the picked list that still show their own bell are what keeps the bubble lit.
        var lit = Ui.VisibleChildren(Grid).Where(s => s.name.StartsWith("warMachineSquare"))
            .Select(s => s.GetComponent<WarMachineInteractionSquare>()?.warMachine)
            .Where(m => m != null && !targets.Contains((int)m.machineCode))
            .Select(m => (int)m.machineCode).ToList();
        foreach (var number in lit)
        {
            var square = FindSquare(number);
            var bell = square?.Find("notificationCircle");
            if (bell == null || !bell.gameObject.activeInHierarchy) continue;
            yield return Ui.Click(square, 1f);
            yield return UpgradeSelected();
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

    private IEnumerator UpgradeSelected()
    {
        var name = Ui.Text(Name);
        if (_levels.Value && Ui.NumberAt(Tokens) > 0)
        {
            yield return Ui.Click(W + "/submenus/submenuButtons/workshop", 1.2f);
            var ups = 0;
            for (var i = 0; i < 20; i++)
            {
                var tokens = Ui.NumberAt(W + "/counters/currencyInteraction (ExpeditionToken)/quantity");
                var cost = Ui.NumberAt(Workshop + "/upgradeButton/costText");
                var componentsOk = Enumerable.Range(0, 3).All(n =>
                {
                    var v = Ui.Text(Workshop + $"/wmComponentProgress ({n})/value");
                    if (string.IsNullOrEmpty(v)) return true;
                    var parts = v.Split('/');
                    return parts.Length == 2 && Ui.Parse(parts[0]) >= Ui.Parse(parts[1]);
                });
                if (cost <= 0 || tokens < cost || !componentsOk || !Ui.Clickable(Workshop + "/upgradeButton")) break;
                var levelBefore = Ui.Text(Level);
                var tokensBefore = tokens;
                var changed = false;
                yield return Ui.ClickUntil(Ui.Find(Workshop + "/upgradeButton"),
                    () => Ui.Text(Level) != levelBefore || Ui.NumberAt(Tokens) < tokensBefore,
                    value => changed = value, $"{name} level upgrade", 1.5f);
                if (!changed)
                {
                    Logger.Warning($"[War Machine] {name}: stopping level upgrades after no verified level or token change.");
                    break;
                }

                ups++;
            }
            _spent += ups;
            if (ups > 0) Logger.Info($"[War Machine] {name}: +{ups} level(s).");
        }

        var mode = (_blueprints.Value ?? "none").Trim().ToLowerInvariant();
        if (mode == "none") yield break;
        var blueprintCurrency = Ui.VisibleChildren(W + "/counters")
            .FirstOrDefault(t => t.name.IndexOf("Blueprint", StringComparison.OrdinalIgnoreCase) >= 0);
        if (blueprintCurrency == null || Ui.NumberOf(blueprintCurrency.Find("quantity")) <= 0) yield break;

        yield return Ui.Click(W + "/submenus/submenuButtons/blueprintUpgrades", 1.2f);
        int[] slots = mode switch { "damage" => new[] { 0 }, "health" => new[] { 1 }, "armor" => new[] { 2 }, _ => new[] { 0, 1, 2 } };
        var bought = 0;
        foreach (var s in slots)
        {
            var button = Blueprints + $"/wmBlueprintUpgrade ({s})/upgradeButton";
            for (var i = 0; i < 20; i++)
            {
                var have = Ui.NumberOf(blueprintCurrency.Find("quantity"));
                var cost = Ui.NumberAt(button + "/costText");
                if (cost <= 0 || have < cost || !Ui.Clickable(button)) break;
                var before = have;
                var changed = false;
                yield return Ui.ClickUntil(Ui.Find(button),
                    () => Ui.NumberOf(blueprintCurrency.Find("quantity")) < before,
                    value => changed = value, $"{name} blueprint {s}", 1.2f);
                if (!changed)
                {
                    Logger.Warning($"[War Machine] {name}: stopping blueprint upgrades after no verified currency change.");
                    break;
                }

                bought++;
            }
        }
        _spent += bought;
        if (bought > 0) Logger.Info($"[War Machine] {name}: {bought} blueprint upgrade(s).");
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

public class ArenaTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/ArenaTokens";

    private const string A = P.Menus + "/ArenaOfKings/bg";
    private const string Grid = A + "/opponentHub/opponentGrid";
    private const string Refresh = A + "/opponentHub/refreshBg/refreshButton";
    private MelonPreferences_Entry<int> _margin;
    private MelonPreferences_Entry<bool> _dailyFight;
    private MelonPreferences_Entry<string> _lastForced;
    private MelonPreferences_Entry<int> _arenaSquad;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _arenaSquad = c.CreateEntry("arena_squad", 0, "Arena Squad",
            "Squad (1, 2 or 3) to fight the arena with. The bot sets it active before arena fights and switches " +
            "back to your normal squad afterwards, so campaign keeps its own squad. 0 = don't switch.");
        _dailyFight = c.CreateEntry("one_fight_daily", true, "One Fight Per Day",
            "If no opponent is beatable all day, fights the weakest one once anyway so the daily ranking reward unlocks.");
        _lastForced = c.CreateEntry("last_forced_fight", "", "Last Forced Fight", "Internal: date of the last forced fight.", true);
        _margin = c.CreateEntry("max_opponent_power_percent", 100, "Max Opponent Power %",
            "Fights only opponents up to this % of your arena power (100 = equal or weaker).\n" +
            "Otherwise uses the free refresh, or waits for the next cycle.");
    }

    public override IEnumerator Execute()
    {
        // Fight with the arena squad, then put the normal squad back.
        var want = _arenaSquad.Value;
        var before = Planner.SquadApplier.ActiveSlot();
        var swap = want is >= 1 and <= 3 && before > 0 && before != want && Planner.SquadApplier.HasMachines(want);
        if (swap) yield return Planner.SquadApplier.SetActive(want, "arena");
        yield return Fight();
        if (swap)
        {
            yield return Watchdog.ForceClearAll("after arena");
            yield return Planner.SquadApplier.SetActive(before, "back from arena");
        }
    }

    private IEnumerator Fight()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.OpenTownBuilding("battles");
        yield return Ui.Click(P.Popups + "/WFMenuSelection/bg/arena", 2.5f);
        if (!Ui.Visible(A)) yield break;

        if (Ui.NumberAt(A + "/battleTokens/quantity") <= 0)
        {
            yield return Ui.Click(P.Menus + "/ArenaOfKings/closeButton");
            yield break;
        }

        // Opponents load a moment after the menu opens.
        for (var w = 0; w < 20 && !Ui.VisibleChildren(Grid).Any(o => Ui.Clickable(o.Find("fightButton"))); w++)
            yield return new WaitForSeconds(0.5f);
        var myPower = Ui.NumberAt(A + "/battleFormation/totalPower");
        Logger.Info($"[Arena] Arena squad power {myPower:N0}, active squad {Planner.SquadApplier.ActiveSlot()}.");
        var limit = myPower > 0 ? myPower * _margin.Value / 100.0 : double.MaxValue;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            // The easiest opponent is the one with the lowest power.
            var target = Ui.VisibleChildren(Grid)
                .Where(o => Ui.Clickable(o.Find("fightButton")))
                .OrderBy(o => Ui.NumberOf(o.Find("powerBg/powerValue")))
                .FirstOrDefault();

            var forceToday = _dailyFight.Value && DateTime.Now.Hour >= 20 &&
                             _lastForced.Value != DateTime.Now.ToString("yyyy-MM-dd") && !Ui.Clickable(Refresh);
            if (target != null && (Ui.NumberOf(target.Find("powerBg/powerValue")) <= limit || forceToday))
            {
                if (Ui.NumberOf(target.Find("powerBg/powerValue")) > limit) _lastForced.Value = DateTime.Now.ToString("yyyy-MM-dd");
                yield return Ui.Click(target.Find("fightButton"), 1.5f);
                // The game shows a preview first; its own Fight button starts the battle.
                yield return Ui.WaitFor(P.Popups + "/AOKBattlePreview/bg", 4f);
                yield return Ui.Click(P.Popups + "/AOKBattlePreview/bg/mask/fightButton", 2f);
                bool? won = null;
                yield return Ui.FinishBattle(r => won = r, 75f);
                if (won == null) yield return new WaitForSeconds(5f);
                NextRunTime = DateTime.Now.AddMinutes(1);
                yield break;
            }

            // No one we can beat: take the free refresh if it is ready, otherwise try again next cycle.
            if (!Ui.Clickable(Refresh)) break;
            yield return Ui.Click(Refresh, 1f);
            for (var w = 0; w < 16 && !Ui.VisibleChildren(Grid).Any(o => Ui.Clickable(o.Find("fightButton"))); w++)
                yield return new WaitForSeconds(0.5f);
        }

        Logger.Debug($"[Arena] No opponent at or under {limit:N0} power; trying again later.");
        NextRunTime = DateTime.Now.AddMinutes(20);
        yield return Ui.Click(P.Menus + "/ArenaOfKings/closeButton");
    }
}

public class OracleBlessingsTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/OracleBlessings";

    private const string O = P.Menus + "/Oracle";
    private const string List = O + "/submenus/bg/blessingsSubmenu/blessings";
    private const string Preview = P.Popups + "/OracleBlessingPreview/bg";
    private const string Bless = Preview + "/darkBlueFrame/buttonRect/buyUpgradeButton";

    public override int MinimumLevel => LevelRequirements.Oracle;

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.OpenTownBuilding("oracle");
        yield return Ui.Click(O + "/submenuButtons/blessings", 1.5f);
        if (!Ui.Visible(List)) yield break;

        foreach (var blessing in Ui.VisibleChildren(List).Where(b => b.name.StartsWith("blessing")).ToList())
        {
            yield return Ui.Click(blessing, 1.2f);
            // The game only enables "Bless" when you have every glyph it asks for.
            var name = Ui.Text(Preview + "/darkBlueFrame/upgradeName");
            for (var n = 0; n < 10 && Ui.Clickable(Bless); n++)
            {
                var levelBefore = Ui.Text(Preview + "/darkBlueFrame/upgradeLevelText");
                var changed = false;
                yield return Ui.ClickUntil(Ui.Find(Bless),
                    () => Ui.Text(Preview + "/darkBlueFrame/upgradeLevelText") != levelBefore,
                    value => changed = value, $"Oracle blessing: {name}", 1.5f);
                if (!changed)
                {
                    Logger.Warning($"[Oracle] Stopping {name}: blessing level did not change after purchase.");
                    break;
                }
            }
            yield return Ui.Click(Preview + "/closeButton", 0.8f);
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

        // Read every upgrade once: name, cost and whether it is maxed.
        var info = new System.Collections.Generic.List<(Transform node, string name, double cost, bool maxed)>();
        var skipped = 0;
        foreach (var node in Ui.VisibleChildren(Nodes).Where(n => n.name.StartsWith("treeOfLifePersonalUpgrade")).ToList())
        {
            if (NodeBuyer.CanSkip(node, Ui.NumberAt(Tokens) - _keepTokens.Value))
            {
                skipped++;
                continue;
            }

            yield return Ui.Click(node, 1f);
            var name = Ui.Text(Preview + "/upgradeName");
            var level = Ui.Text(Preview + "/upgradeLevelText");
            var parts = level.Split('/');
            var maxed = parts.Length == 2 && Ui.Parse(parts[0]) >= Ui.Parse(parts[1]);
            var cost = Ui.NumberAt(Buy + "/cost");
            var done = maxed || !Ui.Visible(Buy);
            NodeBuyer.Remember(node, maxed, done ? 0 : cost);
            info.Add((node, name, cost, done));
            yield return Ui.Click(Preview + "/closeButton", 0.6f);
        }

        Logger.Info($"[Personal Tree] Previewed {info.Count} upgrade(s), skipped {skipped} already known as unaffordable, maxed or locked.");

        var order = (_priority.Value ?? string.Empty).Split(',').Select(s => s.Trim().ToLowerInvariant()).ToList();
        int Rank(string n)
        {
            var i = order.IndexOf((n ?? string.Empty).ToLowerInvariant());
            return i < 0 ? order.Count : i;
        }

        foreach (var up in info.Where(u => !u.maxed && u.cost > 0).OrderBy(u => Rank(u.name)).ThenBy(u => u.cost).ToList())
        {
            if (Ui.NumberAt(Tokens) - _keepTokens.Value < up.cost) continue;
            yield return Ui.Click(up.node, 1f);
            NodeBuyer.Forget(up.node);
            for (var n = 0; n < 5; n++)
            {
                var cost = Ui.NumberAt(Buy + "/cost");
                if (cost <= 0 || Ui.NumberAt(Tokens) - _keepTokens.Value < cost || !Ui.Clickable(Buy)) break;
                var levelBefore = Ui.Text(Preview + "/upgradeLevelText");
                var tokensBefore = Ui.NumberAt(Tokens);
                var changed = false;
                yield return Ui.ClickUntil(Ui.Find(Buy),
                    () => Ui.Text(Preview + "/upgradeLevelText") != levelBefore ||
                          Ui.NumberAt(Buy + "/cost") != cost ||
                          Ui.NumberAt(Tokens) < tokensBefore,
                    value => changed = value, up.name, 1.5f);
                if (!changed)
                {
                    Logger.Warning($"[Personal Tree] Stopping after {up.name} made no verified progress.");
                    if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.6f);
                    yield return Ui.Click(T + "/closeButton");
                    yield break;
                }
            }

            yield return Ui.Click(Preview + "/closeButton", 0.6f);
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
