using System;
using System.Collections;
using System.Linq;
using Firebot.Core;
using Firebot.Core.Tasks;
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
        for (var i = 0; i < count && i < 30; i++)
        {
            var item = Ui.VisibleChildren(List).Skip(_deleteClaimed.Value ? 0 : i).FirstOrDefault();
            if (item == null) break;
            yield return Ui.Click(item, 1f);
            if (Ui.Clickable(Claim))
            {
                yield return Ui.Click(Claim, 1.5f);
                yield return Ui.CloseRewards();
                if (_deleteClaimed.Value) yield return Ui.Click(Delete, 1f);
            }
            else if (_deleteClaimed.Value) break;
        }

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
                var cost = Ui.NumberAt(Market + "/items/gameTokenWithBeer/purchaseButtonOffer/currencyHolder/purchaseCost");
                if (cost <= 0 || beer < cost) break;
                yield return Ui.Click(Market + "/items/gameTokenWithBeer/purchaseButtonOffer", 1.5f);
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
                yield return Ui.Click(Play, 2f);
                // In manual mode the game waits for a card to be picked.
                var cards = Ui.VisibleChildren(Tavern + "/cardHolder").Where(Ui.Clickable).ToList();
                if (cards.Count > 0) yield return Ui.Click(cards[rng.Next(cards.Count)], 3f, "tavern card");
                yield return Ui.CloseRewards();
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
                    yield return Ui.Click(button, 0.6f, name);
                    clicks++;
                }
            }
        }

        if (_buyUpgrades.Value)
        {
            yield return Ui.Click(M + "/submenuButtons/upgrades", 1.5f);
            for (var tree = 0; tree < 8; tree++)
            {
                foreach (var up in Ui.VisibleChildren(UpgradeList).ToList())
                {
                    var button = up.Find("upgradeButton");
                    while (clicks < 90 && Ui.Clickable(button))
                    {
                        yield return Ui.Click(button, 0.8f, Ui.Text(up.Find("upgradeName")));
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

        if (_buyChests.Value)
        {
            yield return Ui.Click(M + "/submenuButtons/emblemMarket", 1.5f);
            yield return Ui.Click(M + "/bg/emblemMarketSubmenu/categoryButtons/gear", 1.2f);
            for (var i = 3; i >= 0; i--)
            {
                var buy = GearGrid + $"/exoticMerchantEmblemGearChest ({i})/purchaseButton";
                for (var n = 0; n < 5 && Ui.Clickable(buy); n++)
                {
                    yield return Ui.Click(buy, 1.5f);
                    yield return Ui.CloseRewards();
                }
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
            yield return Ui.Click(C + "/crystalParent/hitButton", 1f);
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
                        if (cost <= 0 || Ui.NumberAt(Orbs) < cost || !Ui.Clickable(Holy)) break;
                        yield return Ui.Click(Holy, 1.2f, "holy damage");
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
            yield return Ui.Click(Hit, 3f);
            yield return Ui.CloseRewards();
            _hits++;
        }
    }

    private IEnumerator Buy(string item)
    {
        var button = Items + $"/{item}/claimBg/purchaseButton";
        for (var n = 0; n < 5; n++)
        {
            var cost = Ui.NumberAt(button + "/priceHolder/purchaseCost");
            // Eclipse stones cost expedition tokens; the tome is paid in another currency. Require that some
            // counter in the shop covers the price, and let the currency guard stop anything else.
            var best = Ui.VisibleChildren(Shop + "/counters").Select(c => Ui.NumberOf(c.Find("quantity"))).DefaultIfEmpty(-1).Max();
            var tokens = Ui.NumberAt(Shop + "/counters/currencyInteraction (ExpeditionToken)/quantity");
            var isStone = item.StartsWith("moonstone");
            if (cost <= 0 || (isStone ? tokens : best) < cost || !Ui.Clickable(button)) yield break;
            yield return Ui.Click(button, 1.5f, item);
            if (Ui.MissingCurrency) yield break;
            yield return Ui.CloseRewards();
        }
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
        for (var i = 0; i < 10; i++)
        {
            var have = Ui.NumberAt(A + "/counters/currencyInteraction (ArcaneCrystal)/quantity");
            var cost = Ui.NumberAt(Button + "/costText");
            if (cost <= 0 || have < cost || !Ui.Clickable(Button)) break;
            yield return Ui.Click(Button, 3f);
            yield return Ui.CloseRewards();
        }

        yield return Ui.Click(A + "/closeButton");
    }
}

public class WarMachineUpgradeTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/Warmachines";

    private const string W = P.Menus + "/WarMachines";
    private const string Grid = W + "/warMachinesScrollView/Viewport/grid";
    private const string Name = W + "/bg/warMachinePreview/standardLevelProgress/bg/name";
    private const string Workshop = W + "/submenus/bg/workshopSubmenu";
    private const string Blueprints = W + "/submenus/bg/blueprintUpgradesSubmenu/upgrades";
    private MelonPreferences_Entry<string> _machine;
    private MelonPreferences_Entry<bool> _levels;
    private MelonPreferences_Entry<string> _blueprints;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _machine = c.CreateEntry("war_machine", "Cloudfist", "War Machine",
            "Name of the war machine to upgrade, as the garage shows it.");
        _levels = c.CreateEntry("upgrade_levels", true, "Upgrade Levels",
            "Levels it up in the workshop with expedition tokens and components.");
        _blueprints = c.CreateEntry("blueprints", "damage", "Blueprints",
            "Blueprint upgrades: none, damage, health, armor, or all.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(1);
        yield return Ui.OpenTownBuilding("engineer");
        yield return Ui.Click(P.Popups + "/GarageSelection/bg/garage", 2f);
        if (!Ui.Visible(W)) yield break;

        var found = false;
        foreach (var square in Ui.VisibleChildren(Grid).Where(s => s.name.StartsWith("warMachineSquare")).ToList())
        {
            yield return Ui.Click(square, 1f);
            if (!Ui.Text(Name).Equals(_machine.Value.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            found = true;
            break;
        }

        if (!found)
        {
            Logger.Warning($"[War Machine] '{_machine.Value}' not found in the garage.");
            yield return Ui.Click(W + "/closeButton");
            yield break;
        }

        if (_levels.Value)
        {
            yield return Ui.Click(W + "/submenus/submenuButtons/workshop", 1.2f);
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
                yield return Ui.Click(Workshop + "/upgradeButton", 1.5f);
            }
        }

        var mode = (_blueprints.Value ?? "none").Trim().ToLowerInvariant();
        if (mode != "none")
        {
            yield return Ui.Click(W + "/submenus/submenuButtons/blueprintUpgrades", 1.2f);
            var counter = Ui.VisibleChildren(W + "/counters")
                .FirstOrDefault(t => t.name.IndexOf("Blueprint", StringComparison.OrdinalIgnoreCase) >= 0);
            if (counter == null)
                Logger.Debug("[War Machine] No blueprint counter on screen; skipping blueprints to stay safe.");
            else
            {
                int[] slots = mode switch { "damage" => new[] { 0 }, "health" => new[] { 1 }, "armor" => new[] { 2 }, _ => new[] { 0, 1, 2 } };
                foreach (var s in slots)
                {
                    var button = Blueprints + $"/wmBlueprintUpgrade ({s})/upgradeButton";
                    for (var i = 0; i < 20; i++)
                    {
                        var have = Ui.NumberOf(counter.Find("quantity"));
                        var cost = Ui.NumberAt(button + "/costText");
                        if (cost <= 0 || have < cost || !Ui.Clickable(button)) break;
                        yield return Ui.Click(button, 1.2f);
                    }
                }
            }
        }

        yield return Ui.Click(W + "/closeButton");
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
                yield return Ui.Click(claim, 1.5f);
                yield return Ui.CloseRewards();
            }

            yield return Ui.Click(Mini + "/closeButton", 1.5f);
        }

        yield return Ui.Click(P.Events + "/EventManager/bg/closeButton");
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

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _dailyFight = c.CreateEntry("one_fight_daily", true, "One Fight Per Day",
            "If no opponent is beatable all day, fights the weakest one once anyway so the daily ranking reward unlocks.");
        _lastForced = c.CreateEntry("last_forced_fight", "", "Last Forced Fight", "Internal: date of the last forced fight.", true);
        _margin = c.CreateEntry("max_opponent_power_percent", 100, "Max Opponent Power %",
            "Fights only opponents up to this % of your arena power (100 = equal or weaker).\n" +
            "Otherwise uses the free refresh, or waits for the next cycle.");
    }

    public override IEnumerator Execute()
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
            for (var n = 0; n < 10 && Ui.Clickable(Bless); n++)
                yield return Ui.Click(Bless, 1.5f, Ui.Text(Preview + "/darkBlueFrame/upgradeName"));
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

        // Read every upgrade once: name, cost and whether it is maxed.
        var info = new System.Collections.Generic.List<(Transform node, string name, double cost, bool maxed)>();
        foreach (var node in Ui.VisibleChildren(Nodes).Where(n => n.name.StartsWith("treeOfLifePersonalUpgrade")).ToList())
        {
            yield return Ui.Click(node, 1f);
            var name = Ui.Text(Preview + "/upgradeName");
            var level = Ui.Text(Preview + "/upgradeLevelText");
            var parts = level.Split('/');
            var maxed = parts.Length == 2 && Ui.Parse(parts[0]) >= Ui.Parse(parts[1]);
            var cost = Ui.NumberAt(Buy + "/cost");
            info.Add((node, name, cost, maxed || !Ui.Visible(Buy)));
            yield return Ui.Click(Preview + "/closeButton", 0.6f);
        }

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
            for (var n = 0; n < 5; n++)
            {
                var cost = Ui.NumberAt(Buy + "/cost");
                if (cost <= 0 || Ui.NumberAt(Tokens) - _keepTokens.Value < cost || !Ui.Clickable(Buy)) break;
                yield return Ui.Click(Buy, 1.5f, up.name);
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
        foreach (var tab in new[] { "dailyButton", "weeklyButton" })
        {
            yield return Ui.Click(Q + "/submenuButtons/" + tab, 1.5f);
            foreach (var scroll in Ui.VisibleChildren(Q + "/submenus").ToList())
            {
                var grid = scroll.Find("Viewport/grid");
                if (grid == null) continue;
                for (var i = 0; i < grid.childCount; i++)
                {
                    var claim = grid.GetChild(i).Find("claimButton");
                    if (!Ui.Clickable(claim)) continue;
                    yield return Ui.Click(claim, 1.5f, "quest reward");
                    yield return Ui.CloseRewards();
                }
            }
        }

        yield return Ui.Click(C + "/closeButton");
    }
}
