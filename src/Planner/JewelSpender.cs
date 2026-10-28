using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Planner;

/// <summary>
///     "Spend for me" for the planner's ethereal shard advisor: enchants the planned jewels one by one through the
///     Hall of Heroes, the same screens a player uses (hero, Enchanting, Jewels, Enchant). The planner sends the
///     steps in order, already fitted to the shard budget; the bot stops at the first enchant the game will not
///     allow (not enough shards, max level), so it never spends more than the game lets a player spend.
/// </summary>
public static class JewelSpender
{
    private const string Hoh = P.Menus + "/HallOfHeroes";
    private const string EnchantTab = Hoh + "/submenus/submenuButtons/enchanting";

    public static bool Busy { get; private set; }
    public static bool Ok { get; private set; }
    public static string Status { get; private set; } = "";

    private sealed class Step
    {
        public int Hero;
        public JewelSlot Slot;
    }

    /// <summary>Parses "23.Ankh,9.Rune" (hero number, jewel slot), at most 40 steps.</summary>
    public static string Request(string plan)
    {
        if (Busy) return "Already spending.";
        var steps = new List<Step>();
        foreach (var part in (plan ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Take(40))
        {
            var bits = part.Split('.');
            if (bits.Length != 2 || !int.TryParse(bits[0], out var hero)) continue;
            if (!Enum.TryParse<JewelSlot>(bits[1], true, out var slot)) continue;
            steps.Add(new Step { Hero = hero, Slot = slot });
        }
        if (steps.Count == 0) return "Nothing to spend.";
        Busy = true;
        Ok = false;
        Status = "Waiting for the bot to finish its current job…";
        MelonCoroutines.Start(Run(steps));
        return "Spending";
    }

    private static IEnumerator Run(List<Step> steps)
    {
        var giveUp = Time.unscaledTime + 180f;
        while ((BotManager.IsBusy || SquadApplier.Busy) && Time.unscaledTime < giveUp) yield return new WaitForSeconds(0.5f);
        yield return Watchdog.ForceClearAll("before spending ethereal shards");

        Status = "Opening the Hall of Heroes…";
        // Through town, the same way a player gets there (opening it from code left a black screen).
        yield return Ui.OpenTownBuilding("hallOfHeroes");
        HallOfHeroesMech mech = null;
        try { mech = GameInitialize.MenuLoader?.HallOfHeroesMech; }
        catch (Exception e) { Logger.Debug($"[Shards] mech: {e.Message}"); }
        yield return Ui.WaitFor(Hoh, 4f);
        yield return new WaitForSeconds(1f);
        if (mech == null || !Ui.Visible(Hoh))
        {
            Finish(false, "Could not open the Hall of Heroes. No shards were spent.");
            yield break;
        }

        var done = 0;
        int? currentHero = null;
        foreach (var step in steps)
        {
            if (currentHero != step.Hero)
            {
                if (!SelectHero(mech, step.Hero))
                {
                    // The hero list only builds tiles as it scrolls; step through heroes with the arrows instead.
                    for (var n = 0; n < 80 && Selected(mech) != step.Hero; n++)
                    {
                        try { mech.GoForthHero(); } catch { break; }
                        yield return new WaitForSeconds(0.15f);
                    }
                    if (Selected(mech) != step.Hero) { Logger.Info($"[Shards] Could not find hero {step.Hero}; skipped."); continue; }
                }
                currentHero = step.Hero;
                yield return new WaitForSeconds(1.2f);
                yield return Ui.Click(EnchantTab, 1f);
                var vc = UnityEngine.Object.FindObjectOfType<HOHEnchantSubmenuViewController>();
                var jewels = vc?.CategoryJewelsButton;
                if (jewels != null && jewels.gameObject.activeInHierarchy && jewels.interactable) jewels.onClick.Invoke();
                yield return new WaitForSeconds(1f);
            }

            var item = FindItem(step.Slot);
            var button = item?.enchantJewelItemButton;
            var costText = "";
            try { costText = item?.enchantJewelItemCostText?.text ?? ""; } catch { }
            var name = Enum.GetName(typeof(HeroCode), step.Hero) ?? step.Hero.ToString();
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                // Out of shards or at max level: stop here instead of skipping ahead, so the order stays the plan's.
                Finish(done > 0, done > 0
                    ? $"Did {done} enchant(s), then stopped: the game would not enchant {name}'s {step.Slot} jewel{(costText.Length > 0 ? $" (costs {costText})" : "")}. Usually that means not enough shards left, or the jewel is at its max level."
                    : $"The game would not enchant {name}'s {step.Slot} jewel{(costText.Length > 0 ? $" (costs {costText})" : "")}, so nothing was spent. Usually that means not enough shards, or the jewel is at its max level.");
                yield return Watchdog.ForceClearAll("after spending ethereal shards");
                yield break;
            }

            var before = Level(step.Hero, step.Slot);
            button.onClick.Invoke();
            Status = $"Enchanting {name}'s {step.Slot} jewel ({done + 1} of {steps.Count})…";
            var until = Time.unscaledTime + 4f;
            while (Level(step.Hero, step.Slot) <= before && Time.unscaledTime < until) yield return new WaitForSeconds(0.2f);
            if (Level(step.Hero, step.Slot) > before)
            {
                done++;
                Logger.Info($"[Shards] {name}: {step.Slot} jewel {before} -> {before + 1}.");
            }
            yield return new WaitForSeconds(0.5f);
        }

        yield return Watchdog.ForceClearAll("after spending ethereal shards");
        Finish(done > 0, done > 0 ? $"Done: {done} jewel enchant(s)." : "No enchant went through. No shards were spent.");
    }

    private static bool SelectHero(HallOfHeroesMech mech, int hero)
    {
        try
        {
            var list = mech.heroInteractions;
            for (var i = 0; list != null && i < list.Count; i++)
                if (list[i]?.hero != null && list[i].hero.code == hero)
                {
                    mech.SelectHero(i, true);
                    return true;
                }
        }
        catch (Exception e) { Logger.Debug($"[Shards] select: {e.Message}"); }
        return false;
    }

    private static int Selected(HallOfHeroesMech mech)
    {
        try { return mech.heroServerSelected?.code ?? -1; }
        catch { return -1; }
    }

    private static EnchantJewelItemInteraction FindItem(JewelSlot slot)
    {
        var vc = UnityEngine.Object.FindObjectOfType<HOHEnchantSubmenuViewController>();
        var items = vc?.JewelItems;
        for (var i = 0; items != null && i < items.Length; i++)
            if (items[i] != null && items[i].slot == slot) return items[i];
        return null;
    }

    private static int Level(int hero, JewelSlot slot)
    {
        try
        {
            var dict = GameInitialize.HandlerLoader?.heroServerHandler?.heroesDict;
            if (dict == null) return -1;
            foreach (var kv in dict)
                if (kv.Value != null && kv.Value.code == hero && kv.Value.jewelDict != null && kv.Value.jewelDict.ContainsKey(slot))
                    return kv.Value.jewelDict[slot].enchantLevel;
        }
        catch { }
        return -1;
    }

    private static void Finish(bool ok, string message)
    {
        Ok = ok;
        Status = message;
        Busy = false;
        Logger.Info($"[Shards] {message}");
    }
}
