using System;
using System.Collections;
using System.Collections.Generic;
using Firebot.Core;
using Firebot.Core.Tasks;
using MelonLoader;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

public class ChaosRiftTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.ChaosRift;

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
    private readonly Func<DateTime> _now;
    private DateTime _riftRetryAfter;
    private int _hits;

    public ChaosRiftTask() : this(() => DateTime.Now) { }

    internal ChaosRiftTask(Func<DateTime> now)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    protected override string NotificationPath => P.Notifications + "/ChaosRift";
    protected override bool UseScheduledTime => true;
    private static readonly string[] NotificationNames =
        { "ChaosRift", "ChaosRiftSupplies", "GuardianHolyUpgrade" };

    internal override IEnumerable<string> HandledNotificationNames => NotificationNames;
    internal override IEnumerable<KeyValuePair<string, DateTime>> RetryDeadlines
    {
        get
        {
            if (_riftRetryAfter > _now())
                yield return new KeyValuePair<string, DateTime>("RiftRetryAfter", _riftRetryAfter);
        }
    }

    internal override void RestoreRetryDeadline(string name, DateTime when)
    {
        if (name == "RiftRetryAfter") _riftRetryAfter = when;
    }

    protected override bool IsNotificationVisibleCore()
    {
        var now = _now();
        return IsEnabled && now >= BubbleQuietUntil &&
               ((now >= _riftRetryAfter && (Ui.Visible(P.Notifications + "/ChaosRift") ||
                                            Ui.Visible(P.Notifications + "/ChaosRiftSupplies"))) ||
                (_holyUpgrades?.Value == true && Ui.Visible(P.Notifications + "/GuardianHolyUpgrade")));
    }

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
        if (_now() < _riftRetryAfter)
        {
            NextRunTime = _riftRetryAfter;
            if (_holyUpgrades.Value && Ui.Visible(P.Notifications + "/GuardianHolyUpgrade"))
            {
                yield return Ui.Click(P.Notifications + "/GuardianHolyUpgrade", 2f);
                yield return HolyUpgrades();
            }
            yield break;
        }

        NextRunTime = _now().AddHours(1);
        _hits = 0;
        if (Ui.Visible(P.Notifications + "/ChaosRift"))
            yield return Ui.Click(P.Notifications + "/ChaosRift", 2f);
        else
            yield return Ui.OpenGuildBuilding("chaosRift");
        if (!Ui.Visible(R))
        {
            const string message = P.Popups + "/GenericMessage/bg";
            if (Ui.Visible(message))
            {
                var text = Ui.Text(message + "/messageText");
                Logger.Info("[Chaos Rift] The game would not open the Chaos Rift" +
                            (string.IsNullOrWhiteSpace(text) ? "." : $": \"{text.Trim()}\"") +
                            " Checking again in 6 hours.");
                yield return Ui.Click(message + "/closeButton", 1f);
                _riftRetryAfter = _now().AddHours(6);
                NextRunTime = _riftRetryAfter;
            }
            else
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
        if (_hits >= 30) NextRunTime = _now().AddMinutes(2);
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
            () => NextRunTime = _now().AddMinutes(2));
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
