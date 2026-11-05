using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Firebot.Behaviors.Extra;
using Firebot.BotActions;
using Firebot.Core;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Planner;

public static class VoidCrystalSpender
{
    private const string Hall = P.Menus + "/HallOfHeroes";
    private static object _routine;
    private static string _readError;
    private static string _epoch = Guid.NewGuid().ToString("N");
    private static readonly HashSet<Guid> HandledRequests = new();
    public static bool Busy { get; private set; }
    public static bool Ok { get; private set; }
    public static string Status { get; private set; } = "";

    internal static List<VoidCrystalOption> ReadOptions()
    {
        if (!GameInitialize.HasGameLoaded()) throw new InvalidOperationException("The game is still loading.");
        var heroes = GameInitialize.HandlerLoader?.heroServerHandler;
        var deck = GameInitialize.HandlerLoader?.heroDeck?.heroes;
        if (heroes == null || !heroes.hasLoaded || heroes.heroesDict == null || deck == null || deck.Length == 0)
            throw new InvalidOperationException("Hero gear is not loaded yet.");
        var owned = new HashSet<int>();
        foreach (var hero in deck)
            if (hero != null && hero.IsUnlocked().GetDecrypted()) owned.Add(hero.HeroCode);
        if (owned.Count == 0) throw new InvalidOperationException("No unlocked hero data is available yet.");
        var result = new List<VoidCrystalOption>();
        int empty = 0, locked = 0, capped = 0;
        foreach (var pair in heroes.heroesDict)
        {
            var hero = pair.Value;
            if (hero == null || !owned.Contains(hero.code)) continue;
            if (hero.gearsDict == null) throw new InvalidOperationException("Hero gear is not loaded yet.");
            foreach (var entry in hero.gearsDict)
            {
                var item = entry.Value;
                if (item == null || (int)item.itemRarity <= 0) { empty++; continue; }
                // Tier 1 is always open; new accounts can report 0 here before any tier unlock.
                if ((int)item.gearTier > Math.Max(1, hero.gearTier)) { locked++; continue; }
                // Guides: each rarity allows two enchants (Common 2, Uncommon 4, ...). Use that documented cap
                // rather than the game's per-rarity flag, whose live meaning was never confirmed.
                if (item.IsMaxEnchantLvl().GetDecrypted() || item.EnchantLevel >= 2 * (int)item.itemRarity) { capped++; continue; }
                var cost = item.enchantCost.GetDecrypted();
                if (!VoidCrystalPolicy.WholeAmount(cost) || cost <= 0)
                    throw new InvalidOperationException("An enchant price could not be read safely.");
                result.Add(new VoidCrystalOption
                {
                    Hero = hero.code, Name = hero.heroCode.ToString(), Slot = (int)entry.Key,
                    Level = item.EnchantLevel, Cost = cost,
                    CurrentEffect = item.GetCurrentEffectText(), NextEffect = item.NextEnchantEffectText(),
                });
            }
        }
        Skipped = $"{result.Count} upgradable, {capped} at their rarity's enchant cap, {locked} in a locked tier, {empty} empty slots";
        return result;
    }

    internal static string Skipped { get; private set; } = "";
    private static string _loggedSkipped;

    private static double Balance()
    {
        var currencies = GameInitialize.HandlerLoader?.currencyHandler?.currencies;
        if (currencies == null || !currencies.ContainsKey(CurrencyCode.VoidCrystal))
            throw new InvalidOperationException("The Void Crystal balance is not loaded.");
        var balance = currencies[CurrencyCode.VoidCrystal].amount;
        if (!VoidCrystalPolicy.WholeAmount(balance))
            throw new InvalidOperationException("The Void Crystal balance could not be read safely.");
        return balance;
    }

    internal static void WriteSnapshot(Utf8JsonWriter writer)
    {
        List<VoidCrystalOption> items = null;
        var error = "";
        double balance = 0;
        try
        {
            items = ReadOptions(); balance = Balance(); _readError = null;
            if (_loggedSkipped != Skipped) { _loggedSkipped = Skipped; Logger.Info("[Crystals] Gear scan: " + Skipped + "."); }
        }
        catch (Exception e)
        {
            error = e.Message;
            if (_readError != error) Logger.Warning($"[Crystals] Advisor unavailable: {error}");
            _readError = error;
        }
        writer.WriteStartObject("crystals");
        writer.WriteString("epoch", _epoch);
        writer.WriteBoolean("available", items != null && error.Length == 0);
        writer.WriteString("error", error);
        writer.WriteBoolean("busy", Busy);
        writer.WriteBoolean("ok", Ok);
        writer.WriteString("status", Status);
        writer.WriteNumber("balance", balance);
        writer.WriteString("scan", error.Length == 0 ? Skipped : "");
        writer.WriteStartArray("items");
        if (error.Length == 0 && items != null)
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteNumber("hero", item.Hero);
                writer.WriteString("name", item.Name);
                writer.WriteNumber("slot", item.Slot);
                writer.WriteNumber("level", item.Level);
                writer.WriteNumber("cost", item.Cost);
                writer.WriteString("current", item.CurrentEffect);
                writer.WriteString("next", item.NextEffect);
                writer.WriteEndObject();
            }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public static string Request(string json)
    {
        var started = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("epoch").GetString() != _epoch) return Reject("The account or game session changed. Refresh first.");
            var action = root.GetProperty("action").GetString();
            if (action == "stop") { Cancel("Stopped by you. Refresh before starting again; a pending enchant may still finish."); return Status; }
            if (action != "start") return Reject("Unknown crystal action.");
            if (!Guid.TryParse(root.GetProperty("requestId").GetString(), out var requestId) || HandledRequests.Contains(requestId))
                return Reject("This spending request is invalid or has already been handled. Refresh first.");
            var goal = root.GetProperty("goal").GetString();
            var target = root.GetProperty("target").GetInt32();
            var budget = root.GetProperty("budget").GetDouble();
            if (!VoidCrystalPolicy.ValidGoal(goal) || target < -1 ||
                !VoidCrystalPolicy.WholeAmount(budget) || budget <= 0) return Reject("Choose a goal and a positive whole-crystal budget.");
            if (Busy || SquadApplier.Busy || JewelSpender.Busy) return Reject("Another planner action is running. Wait for it to finish.");
            if (GameTutorial.ShouldHold) return Reject("Finish the game's tutorial step before spending.");
            var options = ReadOptions();
            var balance = Balance();
            if (budget > balance) return Reject("Your budget exceeds your current Void Crystals.");
            if (!VoidCrystalPolicy.Rank(options, goal, target).Any(o => o.Cost <= budget))
                return Reject("No unlocked enchant for this goal fits the budget.");
            Busy = true;
            started = true;
            HandledRequests.Add(requestId);
            Ok = false;
            Status = "Waiting for the bot to finish its current job...";
            _routine = MelonCoroutines.Start(CoroutineGuard.Run(Run(goal, target, budget, _epoch), "Void Crystal spending",
                () => Finish(false, "Stopped after an error. A pending enchant may still finish; refresh before trying again.")));
            return "Spending";
        }
        catch (Exception e)
        {
            if (started) Finish(false, "Could not start crystal spending: " + e.Message);
            return Reject("Could not start: " + e.Message);
        }
    }

    private static string Reject(string message)
    {
        Logger.Warning("[Crystals] " + message);
        return message;
    }

    public static void Cancel(string reason = "Stopped because the game scene changed. A pending enchant may still finish.")
    {
        _epoch = Guid.NewGuid().ToString("N");
        HandledRequests.Clear();
        if (!Busy) return;
        var routine = _routine;
        _routine = null;
        try { if (routine != null) MelonCoroutines.Stop(routine); }
        finally { Finish(false, reason); }
    }

    private static bool Safe(string epoch) => epoch == _epoch && GameInitialize.HasGameLoaded() && !GameTutorial.ShouldHold;

    private static IEnumerator Run(string goal, int target, double budget, string epoch)
    {
        var spent = 0d;
        var done = 0;
        try
        {
            var until = Time.unscaledTime + 180f;
            while ((BotManager.IsBusy || AutoUpgrade.IsBalancedCycleActive) && Time.unscaledTime < until)
                yield return new WaitForSeconds(0.25f);
            if (BotManager.IsBusy || AutoUpgrade.IsBalancedCycleActive || !Safe(epoch))
            { Finish(false, "Could not get a safe turn. No crystals were spent."); yield break; }
            yield return Watchdog.ForceClearAll("before spending Void Crystals");
            if (!Safe(epoch)) { Finish(false, "The game state changed. No crystals were spent."); yield break; }
            yield return Ui.OpenTownBuilding("hallOfHeroes");
            yield return Ui.WaitFor(Hall, 4f);
            var mech = GameInitialize.MenuLoader?.HallOfHeroesMech;
            if (mech == null || !Ui.Visible(Hall))
            { Finish(false, "Could not open the Hall of Heroes. No crystals were spent."); yield break; }

            while (done < VoidCrystalPolicy.MaxEnchants && Safe(epoch))
            {
                var balance = Balance();
                var choice = VoidCrystalPolicy.Rank(ReadOptions(), goal, target).FirstOrDefault(o => o.Cost <= budget - spent && o.Cost <= balance);
                if (choice == null) break;
                Status = $"Finding {choice.Name}'s {(GearSlot)choice.Slot}...";
                if (mech.heroServerSelected?.code != choice.Hero)
                {
                    var found = false;
                    var tiles = mech.heroInteractions;
                    for (var i = 0; tiles != null && i < tiles.Count; i++)
                        if (tiles[i]?.hero?.code == choice.Hero) { mech.SelectHero(i, true); found = true; break; }
                    if (found) yield return new WaitForSeconds(1f);
                    for (var n = 0; n < 80 && Safe(epoch) && mech.heroServerSelected?.code != choice.Hero; n++)
                    {
                        if (!mech.heroSelectionLoading) mech.GoForthHero();
                        yield return new WaitForSeconds(0.25f);
                    }
                }
                if (!Safe(epoch) || mech.heroSelectionLoading || mech.heroServerSelected?.code != choice.Hero)
                { Finish(false, $"Stopped: could not confirm {choice.Name}. Confirmed spending: {spent:N0} crystals."); yield break; }
                yield return Ui.Click(Hall + "/submenus/submenuButtons/enchanting", 0.7f);
                var view = UnityEngine.Object.FindObjectOfType<HOHEnchantSubmenuViewController>();
                if (view?.CategoryGearButton == null || !view.CategoryGearButton.gameObject.activeInHierarchy)
                { Finish(false, $"Gear enchanting is not available. Confirmed spending: {spent:N0} crystals."); yield break; }
                if (view.CategoryGearButton.interactable) view.CategoryGearButton.onClick.Invoke();
                yield return new WaitForSeconds(0.7f);

                EnchantGearItemInteraction interaction = null;
                foreach (var item in view.GearItems)
                    if (item != null && (int)item.slot == choice.Slot) { interaction = item; break; }
                var current = ReadOptions().FirstOrDefault(o => o.Hero == choice.Hero && o.Slot == choice.Slot);
                balance = Balance();
                var gear = interaction?.gearItem;
                var button = interaction?.enchantGearItemButton;
                if (!Safe(epoch) || mech.heroSelectionLoading || mech.heroServerSelected?.code != choice.Hero ||
                    gear == null || (int)gear.heroId != choice.Hero || (int)gear.gearSlot != choice.Slot ||
                    gear.EnchantLevel != choice.Level || gear.enchantCost.GetDecrypted() != choice.Cost ||
                    button == null || !button.gameObject.activeInHierarchy || !button.interactable ||
                    !VoidCrystalPolicy.CanSpend(choice, current, budget, spent, balance))
                { Finish(false, $"Stopped: gear, price or balance changed. Confirmed spending: {spent:N0} crystals. Refresh before trying again."); yield break; }

                Status = $"Enchanting {choice.Name}'s {(GearSlot)choice.Slot} for {choice.Cost:N0} crystals...";
                button.onClick.Invoke();
                until = Time.unscaledTime + 8f;
                var confirmed = false;
                while (Safe(epoch) && Time.unscaledTime < until)
                {
                    var liveGear = GameInitialize.HandlerLoader.heroServerHandler.heroesDict[(HeroCode)choice.Hero].gearsDict[(GearSlot)choice.Slot];
                    if (VoidCrystalPolicy.Confirmed(choice.Level, liveGear.EnchantLevel, balance, Balance(), choice.Cost))
                    { confirmed = true; break; }
                    yield return new WaitForSeconds(0.2f);
                }
                if (!confirmed)
                {
                    Finish(false, $"Stopped: the last enchant could not be confirmed and will not be retried. Earlier confirmed spending: {spent:N0} crystals.");
                    yield break;
                }
                spent += choice.Cost;
                done++;
                Logger.Info($"[Crystals] {choice.Name} {(GearSlot)choice.Slot}: {choice.Level} -> {choice.Level + 1}, cost {choice.Cost:N0}; budget used {spent:N0}/{budget:N0}.");
                yield return new WaitForSeconds(0.3f);
            }
            if (!Safe(epoch)) { Finish(false, $"Stopped: the game state changed. Confirmed spending: {spent:N0} crystals."); yield break; }
            Finish(true, done == 0 ? "No affordable upgrades remain for this goal. No crystals were spent."
                : $"Done: {done} enchants, {spent:N0} of {budget:N0} crystals spent." +
                  (done == VoidCrystalPolicy.MaxEnchants ? " Reached the 40-enchant safety limit." : " No more matching upgrades fit the remaining budget."));
        }
        finally { Busy = false; _routine = null; }
    }

    private static void Finish(bool ok, string message)
    {
        Ok = ok;
        Status = message;
        Busy = false;
        Logger.Info("[Crystals] " + message);
    }
}
