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

    internal sealed class Scan
    {
        public readonly List<VoidCrystalOption> Items = new();
        public readonly HashSet<int> Owned = new();
        public bool DamageReady;
        public bool HealthReady;
        public bool ArmorReady;
        public int OwnedGear;
        public int RawRows;
        public int RawGear;
        public string EffectTable;

        public bool Ready(string goal, int target) => (target == -1 || Owned.Contains(target)) &&
            (target != -1 || (goal switch
        {
            "damage" => DamageReady,
            "health" => HealthReady,
            "armor" => ArmorReady,
            _ => true,
        }));
    }

    private sealed class Party
    {
        public readonly HashSet<int> Active = new();
        public Dictionary<int, double> Damage;
        public Dictionary<int, double> Health;
        public Dictionary<int, double> Armor;
    }

    private static double StatLog(double mantissa, long exponent)
    {
        if (mantissa == 0) return double.NegativeInfinity;
        return mantissa > 0 && double.IsFinite(mantissa)
            ? Math.Log10(mantissa) + exponent : double.NaN;
    }

    private static Dictionary<int, double> Shares(Dictionary<int, double> values)
    {
        if (values.Count == 0 || values.Values.Any(double.IsNaN)) return null;
        var max = values.Values.Max();
        if (!double.IsFinite(max)) return null;
        var scaled = values.ToDictionary(p => p.Key, p => Math.Pow(10, p.Value - max));
        var sum = scaled.Values.Sum();
        if (!double.IsFinite(sum) || sum <= 0) return null;
        return scaled.ToDictionary(p => p.Key, p => p.Value / sum);
    }

    private static Party ReadParty(HashSet<int> owned)
    {
        var party = new Party();
        var battle = GameInitialize.HandlerLoader?.MainBattle;
        var team = GameInitialize.battleCharactersHolder?.heroesTeamLogic;
        if (battle == null || team == null) return party;
        foreach (var id in owned)
            if (battle.IsHeroFighting(id)) party.Active.Add(id);
        if (party.Active.Count == 0) return party;
        var damage = new Dictionary<int, double>();
        var health = new Dictionary<int, double>();
        var armor = new Dictionary<int, double>();
        var logics = team.GetHeroLogics();
        if (logics == null) return party;
        foreach (var logic in logics)
        {
            var hero = logic?.battleHero;
            if (hero?.hero == null) continue;
            var id = hero.hero.HeroCode;
            if (!party.Active.Contains(id)) continue;
            if (damage.ContainsKey(id)) return party;
            var d = hero.DPS;
            var h = hero.maximumHealthTotal;
            var a = hero.armorTotal;
            damage[id] = StatLog(d.Mantissa.GetDecrypted(), d.Exponent.GetDecrypted());
            health[id] = StatLog(h.Mantissa.GetDecrypted(), h.Exponent.GetDecrypted());
            armor[id] = StatLog(a.Mantissa.GetDecrypted(), a.Exponent.GetDecrypted());
        }
        if (damage.Count != party.Active.Count) return party;
        party.Damage = Shares(damage);
        party.Health = Shares(health);
        party.Armor = Shares(armor);
        return party;
    }

    private static bool Nearly(double a, double b) =>
        double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= 1e-8 * Math.Max(1, Math.Abs(b));

    internal static Scan ReadOptions()
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
        var party = ReadParty(owned);
        var result = new Scan
        {
            DamageReady = party.Damage != null,
            HealthReady = party.Health != null,
            ArmorReady = party.Armor != null,
        };
        result.Owned.UnionWith(owned);
        var enchantEffects = GearEffects.enchantEffects;
        var rarityEffects = GearEffects.allGearRarityEffects;
        if (enchantEffects == null || rarityEffects == null)
            throw new InvalidOperationException("The game's gear effect tables are not loaded yet.");
        var levels = new Dictionary<int, double>();
        foreach (var pair in enchantEffects)
            levels[pair.Key.GetDecrypted()] = (double)pair.Value;
        if (!levels.ContainsKey(0)) levels.Add(0, 1);
        result.EffectTable = string.Join(", ", levels.Where(p => p.Key is >= 0 and <= 4)
            .OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value:R}"));
        int empty = 0, locked = 0, capped = 0;
        var scannedHeroes = new HashSet<int>();
        foreach (var pair in heroes.heroesDict)
        {
            var hero = pair.Value;
            if (hero == null || !owned.Contains(hero.code)) continue;
            if ((int)pair.Key != hero.code || !scannedHeroes.Add(hero.code))
                throw new InvalidOperationException("The unlocked hero list is inconsistent.");
            if (hero.heroGears == null) throw new InvalidOperationException("Hero gear is not loaded yet.");
            if (hero.gearsRaw != null)
                foreach (var raw in hero.gearsRaw)
                    if (raw != null)
                    {
                        result.RawRows++;
                        if (raw.r > 0) result.RawGear++;
                    }
            var slots = new HashSet<int>();
            var ownedSlots = new HashSet<int>();
            foreach (var item in hero.heroGears)
            {
                if (item == null) { empty++; continue; }
                var slot = (int)item.gearSlot;
                if ((int)item.heroId != hero.code || slot is < 0 or > 7 || !slots.Add(slot))
                    throw new InvalidOperationException("A hero's gear list is inconsistent.");
                if ((int)item.itemRarity <= 0) { empty++; continue; }
                result.OwnedGear++;
                ownedSlots.Add(slot);
                // Tier 1 is always open; new accounts can report 0 here before any tier unlock.
                if ((int)item.gearTier > Math.Max(1, hero.gearTier)) { locked++; continue; }
                // Guides: each rarity allows two enchants (Common 2, Uncommon 4, ...). Use that documented cap
                // rather than the game's per-rarity flag, whose live meaning was never confirmed.
                if (item.IsMaxEnchantLvl().GetDecrypted() || item.EnchantLevel >= 2 * (int)item.itemRarity) { capped++; continue; }
                var cost = item.enchantCost.GetDecrypted();
                if (!VoidCrystalPolicy.WholeAmount(cost) || cost <= 0)
                    throw new InvalidOperationException("An enchant price could not be read safely.");
                if (!levels.TryGetValue(item.EnchantLevel, out var currentLevel) ||
                    !levels.TryGetValue(item.EnchantLevel + 1, out var nextLevel) ||
                    !rarityEffects.ContainsKey(item.gearSlot) ||
                    !rarityEffects[item.gearSlot].ContainsKey(item.itemRarity))
                    throw new InvalidOperationException("A gear item's enchant details are not loaded yet.");
                var rarity = (double)rarityEffects[item.gearSlot][item.itemRarity];
                var currentFactor = rarity * currentLevel;
                var nextFactor = rarity * nextLevel;
                var nativeEffect = (double)item.effect;
                var power = item.power.GetDecrypted();
                var effectGain = (nextFactor - currentFactor) * 100;
                var multiplierGain = nextFactor / currentFactor - 1;
                if (!double.IsFinite(currentFactor) || currentFactor < 1 ||
                    !double.IsFinite(nextFactor) || nextFactor <= currentFactor ||
                    !double.IsFinite(power) || power <= 0 || !double.IsFinite(effectGain) ||
                    !double.IsFinite(multiplierGain) || multiplierGain <= 0 ||
                    !(Nearly(nativeEffect, currentFactor) || Nearly(nativeEffect, currentFactor - 1) ||
                      Nearly(nativeEffect, (currentFactor - 1) * 100)))
                    throw new InvalidOperationException("A gear item's current effect or next upgrade could not be verified.");
                result.Items.Add(new VoidCrystalOption
                {
                    Hero = hero.code, Name = hero.heroCode.ToString(), Slot = slot,
                    Level = item.EnchantLevel, Cost = cost,
                    CurrentEffect = item.GetCurrentEffectText(), NextEffect = item.NextEnchantEffectText(),
                    Rarity = (int)item.itemRarity, NativeEffect = nativeEffect,
                    EffectGain = effectGain, MultiplierGain = multiplierGain,
                    PowerGain = power, Active = party.Active.Contains(hero.code),
                    DamageReady = result.DamageReady, HealthReady = result.HealthReady, ArmorReady = result.ArmorReady,
                    DamageShare = party.Damage?.GetValueOrDefault(hero.code) ?? 0,
                    HealthShare = party.Health?.GetValueOrDefault(hero.code) ?? 0,
                    ArmorShare = party.Armor?.GetValueOrDefault(hero.code) ?? 0,
                });
            }
            if (hero.gearsRaw != null)
                foreach (var raw in hero.gearsRaw)
                    if (raw != null && raw.r > 0 && !ownedSlots.Contains(raw.s))
                        throw new InvalidOperationException("The game's hero gear is still loading. Try again shortly.");
        }
        if (!scannedHeroes.SetEquals(owned))
            throw new InvalidOperationException("Some unlocked heroes have not finished loading their gear.");
        Skipped = $"{result.Items.Count} upgradable, {capped} at their rarity's enchant cap, {locked} in a locked tier, {empty} empty slots";
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
        Scan scan = null;
        var error = "";
        double balance = 0;
        try
        {
            scan = ReadOptions(); balance = Balance(); _readError = null;
            var detail = Skipped + $"; {scan.RawRows} server gear records, {scan.RawGear} with a rarity; " +
                $"enchant multipliers {scan.EffectTable}";
            if (_loggedSkipped != detail) { _loggedSkipped = detail; Logger.Info("[Crystals] Gear scan: " + detail + "."); }
        }
        catch (Exception e)
        {
            error = e.Message;
            if (_readError != error) Logger.Warning($"[Crystals] Advisor unavailable: {error}");
            _readError = error;
        }
        writer.WriteStartObject("crystals");
        writer.WriteString("epoch", _epoch);
        writer.WriteBoolean("available", scan != null && error.Length == 0);
        writer.WriteString("error", error);
        writer.WriteBoolean("busy", Busy);
        writer.WriteBoolean("ok", Ok);
        writer.WriteString("status", Status);
        writer.WriteNumber("balance", balance);
        writer.WriteString("scan", error.Length == 0 ? Skipped : "");
        writer.WriteNumber("ownedGear", scan?.OwnedGear ?? 0);
        writer.WriteStartObject("party");
        writer.WriteBoolean("damage", scan?.DamageReady == true);
        writer.WriteBoolean("health", scan?.HealthReady == true);
        writer.WriteBoolean("armor", scan?.ArmorReady == true);
        writer.WriteEndObject();
        writer.WriteStartArray("items");
        if (error.Length == 0 && scan != null)
            foreach (var item in scan.Items)
            {
                writer.WriteStartObject();
                writer.WriteNumber("hero", item.Hero);
                writer.WriteString("name", item.Name);
                writer.WriteNumber("slot", item.Slot);
                writer.WriteNumber("level", item.Level);
                writer.WriteNumber("cost", item.Cost);
                writer.WriteString("current", item.CurrentEffect);
                writer.WriteString("next", item.NextEffect);
                writer.WriteNumber("effectGain", item.EffectGain);
                writer.WriteNumber("multiplierGain", item.MultiplierGain);
                writer.WriteNumber("powerGain", item.PowerGain);
                writer.WriteBoolean("active", item.Active);
                writer.WriteBoolean("damageReady", item.DamageReady);
                writer.WriteBoolean("healthReady", item.HealthReady);
                writer.WriteBoolean("armorReady", item.ArmorReady);
                writer.WriteNumber("damageShare", item.DamageShare);
                writer.WriteNumber("healthShare", item.HealthShare);
                writer.WriteNumber("armorShare", item.ArmorShare);
                writer.WriteStartObject("values");
                foreach (var goal in new[] { "balanced", "damage", "health", "armor", "gold", "power" })
                {
                    writer.WriteStartObject(goal);
                    writer.WriteNumber("all", VoidCrystalPolicy.Score(item, goal, -1));
                    writer.WriteNumber("hero", VoidCrystalPolicy.Score(item, goal, item.Hero));
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
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
            if (target >= 0 && !options.Owned.Contains(target)) return Reject("That hero is not in your unlocked roster.");
            if (!options.Ready(goal, target)) return Reject("Current party stats are not ready. Return to the main battle and try again.");
            if (!VoidCrystalPolicy.Rank(options.Items, goal, target).Any(o => o.Cost <= budget))
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

    private static GearItem LiveGear(int hero, int slot)
    {
        var heroes = GameInitialize.HandlerLoader?.heroServerHandler?.heroesDict;
        if (heroes == null || !heroes.ContainsKey((HeroCode)hero)) return null;
        var gears = heroes[(HeroCode)hero]?.heroGears;
        if (gears == null) return null;
        foreach (var gear in gears)
            if (gear != null && (int)gear.heroId == hero && (int)gear.gearSlot == slot) return gear;
        return null;
    }

    private static IEnumerator Run(string goal, int target, double budget, string epoch)
    {
        var spent = 0d;
        var done = 0;
        var reselections = 0;
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
                var scan = ReadOptions();
                if (!scan.Ready(goal, target))
                { Finish(false, $"Stopped: current party stats are not ready. Confirmed spending: {spent:N0} crystals."); yield break; }
                var choice = VoidCrystalPolicy.Rank(scan.Items, goal, target).FirstOrDefault(o => o.Cost <= budget - spent && o.Cost <= balance);
                if (choice == null) break;
                Status = $"Finding {choice.Name}'s {(GearSlot)choice.Slot}...";
                var selected = false;
                yield return HeroSelection.Select(mech, choice.Hero, () => Safe(epoch), value => selected = value);
                if (!selected || !Safe(epoch) || mech.heroSelectionLoading || mech.heroServerSelected?.code != choice.Hero)
                { Finish(false, $"Stopped: could not confirm {choice.Name}. Confirmed spending: {spent:N0} crystals."); yield break; }
                yield return Ui.Click(Hall + "/submenus/submenuButtons/enchanting", 0.7f);
                var view = UnityEngine.Object.FindObjectOfType<HOHEnchantSubmenuViewController>();
                if (view == null)
                { Finish(false, $"Gear enchanting is not available. Confirmed spending: {spent:N0} crystals."); yield break; }
                var gearCategory = view.CategoryGearButton;
                if (gearCategory != null && gearCategory.gameObject.activeInHierarchy &&
                    gearCategory.enabled && gearCategory.interactable)
                    gearCategory.onClick.Invoke();
                yield return new WaitForSeconds(0.7f);

                if (view.CategoryGearObj == null || !view.CategoryGearObj.activeInHierarchy || view.GearItems == null)
                { Finish(false, $"Gear enchanting is not visible. Confirmed spending: {spent:N0} crystals."); yield break; }
                EnchantGearItemInteraction interaction = null;
                foreach (var item in view.GearItems)
                    if (item != null && (int)item.slot == choice.Slot) { interaction = item; break; }
                var refreshed = ReadOptions();
                balance = Balance();
                if (!refreshed.Ready(goal, target))
                { Finish(false, $"Stopped: current party stats changed. Confirmed spending: {spent:N0} crystals."); yield break; }
                var best = VoidCrystalPolicy.Rank(refreshed.Items, goal, target)
                    .FirstOrDefault(o => o.Cost <= budget - spent && o.Cost <= balance);
                var current = refreshed.Items.FirstOrDefault(o => o.Hero == choice.Hero && o.Slot == choice.Slot);
                var gear = interaction?.gearItem;
                var button = interaction?.enchantGearItemButton;
                if (!Safe(epoch) || mech.heroSelectionLoading || mech.heroServerSelected?.code != choice.Hero ||
                    gear == null || (int)gear.heroId != choice.Hero || (int)gear.gearSlot != choice.Slot ||
                    gear.EnchantLevel != choice.Level || gear.enchantCost.GetDecrypted() != choice.Cost ||
                    (int)gear.itemRarity != choice.Rarity || (double)gear.effect != choice.NativeEffect ||
                    gear.power.GetDecrypted() != choice.PowerGain ||
                    button == null || !button.gameObject.activeInHierarchy || !button.enabled || !button.interactable ||
                    !VoidCrystalPolicy.CanSpend(choice, current, budget, spent, balance))
                { Finish(false, $"Stopped: gear, price or balance changed. Confirmed spending: {spent:N0} crystals. Refresh before trying again."); yield break; }
                if (best == null || best.Hero != choice.Hero || best.Slot != choice.Slot)
                {
                    if (++reselections >= 10)
                    { Finish(false, $"Stopped: the best upgrade keeps changing. Confirmed spending: {spent:N0} crystals."); yield break; }
                    continue;
                }
                reselections = 0;

                Status = $"Enchanting {choice.Name}'s {(GearSlot)choice.Slot} for {choice.Cost:N0} crystals...";
                button.onClick.Invoke();
                until = Time.unscaledTime + 8f;
                var confirmed = false;
                while (Safe(epoch) && Time.unscaledTime < until)
                {
                    var liveGear = LiveGear(choice.Hero, choice.Slot);
                    if (liveGear != null && VoidCrystalPolicy.Confirmed(choice.Level, liveGear.EnchantLevel, balance, Balance(), choice.Cost))
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
