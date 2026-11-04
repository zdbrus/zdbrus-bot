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
using ResearchQuantity = Il2Cpp.MeteoriteResearchPreviewPopup.ResearchQuantity;

namespace Firebot.Behaviors.Extra;

public class MeteoriteResearchTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.Library;
    protected override string NotificationPath => P.Notifications + "/MeteoriteResearch";
    protected override bool UseScheduledTime => true;

    private const string Library = P.Menus + "/Library";
    private const string Research = Library + "/submenus/meteoriteResearch";
    private const string Preview = P.Popups + "/MeteoriteResearchPreview/bg";
    private const string Buy = Preview + "/unlocked/researchButton";
    private MelonPreferences_Entry<int> _keep;
    private MelonPreferences_Entry<string> _priority;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _keep = c.CreateEntry("keep_meteorites", 0, "Meteorites To Keep",
            "Minimum meteorites left after each research purchase. Checked again before every purchase.");
        _priority = c.CreateEntry("priority",
            "Attribute damage,All main attributes,Raining gold,Hero level up cost,Attribute health,Attribute armor,Firestone finder,Powerless boss,Powerless enemy",
            "Priority", "Meteorite research upgrades are filled in this order; lower tiers wait until higher tiers are maxed.");
    }

    private static MeteoriteResearchPreviewPopup Popup =>
        GameInitialize.MenuLoader?.MeteoriteResearchPreviewPopup;

    private static bool SingleQuantity => Popup?.currentResearchQuantityEnum == ResearchQuantity.ResearchX1 &&
                                          Popup.currentResearchQuantity == 1;

    private static double Balance()
    {
        var currencies = GameInitialize.HandlerLoader?.currencyHandler?.currencies;
        return currencies != null && currencies.TryGetValue(CurrencyCode.Meteorite, out var currency) &&
               currency != null ? currency.amount : double.NaN;
    }

    private static bool Selected(MeteoriteResearch research)
    {
        var selected = Popup?.meteoriteResearch;
        return Ui.Visible(Preview) && selected != null && selected.Pointer == research.Pointer &&
               selected.index == research.index &&
               selected.researchTree == research.researchTree &&
               selected.meteoriteResearchType == research.meteoriteResearchType;
    }

    private static bool TryReadNode(Transform node, out MeteoriteResearch research, out string name)
    {
        research = node?.GetComponent<MeteoriteResearchInteraction>()?.meteoriteResearch;
        name = research == null || string.IsNullOrWhiteSpace(research.name)
            ? null : Il2CppHolydayStudios.Translator.Translate(research.name)?.Trim();
        return research != null && research.level >= 0 && research.maxLevel > 0 &&
               !string.IsNullOrWhiteSpace(name) && !name.Contains('/');
    }

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(20);
        NextRunTime = DateTime.Now.AddHours(4);
        yield return Ui.OpenTownBuilding("library");
        yield return Ui.WaitFor(Library);
        yield return Ui.Click(Library + "/submenuButtons/meteoriteResearch", 1.5f);
        if (!Ui.Visible(Research)) yield break;

        var balance = Balance();
        if (!double.IsFinite(balance) || balance < 0 || _keep.Value < 0)
        {
            Logger.Warning("[Meteorite Research] Balance or reserve unreadable; no meteorites spent.");
            yield return Ui.Click(Library + "/closeButton");
            yield break;
        }
        if (balance <= _keep.Value)
        {
            Logger.Info($"[Meteorite Research] Keeping {balance} meteorites; reserve is {_keep.Value}.");
            yield return Ui.Click(Library + "/closeButton");
            yield break;
        }

        var tree = Ui.VisibleChildren(Research + "/submenus").FirstOrDefault();
        var candidates = new List<(Transform node, MeteoriteResearch research, string name)>();
        if (tree == null)
        {
            Logger.Warning("[Meteorite Research] Research tree unreadable; no meteorites spent.");
            yield return Ui.Click(Library + "/closeButton");
            yield break;
        }
        for (var i = 0; i < tree.childCount; i++)
        {
            var node = tree.GetChild(i);
            if (!node.name.StartsWith("research") || node.name.StartsWith("researchPath") ||
                !node.gameObject.activeInHierarchy) continue;
            if (!TryReadNode(node, out var research, out var name))
            {
                Logger.Warning("[Meteorite Research] Research data unreadable; priorities left untouched.");
                yield return Ui.Click(Library + "/closeButton");
                yield break;
            }
            if (research.IsUnlocked() && !research.IsMaxLevel()) candidates.Add((node, research, name));
        }

        var order = (_priority.Value ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0).ToList();
        foreach (var candidate in candidates.OrderBy(c => PriorityTier.Rank(order, c.name))
                     .ThenBy(c => c.research.levelUpCost.GetDecrypted()))
        {
            if (Ui.Visible(Preview))
            {
                Logger.Warning("[Meteorite Research] Another preview is open; stopping without spending.");
                break;
            }
            var opened = false;
            yield return Ui.ClickIf(candidate.node,
                () => candidate.research.IsUnlocked() && !candidate.research.IsMaxLevel(),
                value => opened = value, 0.9f);
            if (!opened || !Selected(candidate.research) ||
                !string.Equals(Ui.Text(Preview + "/researchName")?.Trim(), candidate.name,
                    StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning("[Meteorite Research] Selected research changed; stopping without spending.");
                break;
            }

            var action = PriorityTierAction.Wait;
            yield return Upgrade(candidate.research, candidate.name, value => action = value);
            if (Selected(candidate.research)) yield return Ui.Click(Preview + "/closeButton", 0.5f);
            if (action == PriorityTierAction.Advance) continue;
            if (action == PriorityTierAction.RetrySoon)
                BubbleQuietUntil = NextRunTime = DateTime.Now.AddMinutes(2);
            Logger.Info($"[Meteorite Research] Holding at {candidate.name}; lower priorities stay untouched.");
            break;
        }
        if (Ui.Visible(Library)) yield return Ui.Click(Library + "/closeButton");
    }

    private IEnumerator Upgrade(MeteoriteResearch research, string name, Action<PriorityTierAction> result)
    {
        for (var purchases = 0; purchases < 10; purchases++)
        {
            if (research.IsMaxLevel()) { result(PriorityTierAction.Advance); yield break; }
            // Native cost is recalculated for the selected quantity. Never use a rounded UI price or a bulk buy.
            for (var attempts = 0; attempts < 8 && Selected(research) && !SingleQuantity; attempts++)
            {
                var changed = false;
                yield return Ui.ClickIf(Preview + "/unlocked/changeQuantityButton", () => Selected(research),
                    value => changed = value, 0.3f);
                if (!changed) break;
            }
            if (!Selected(research) || !SingleQuantity)
            {
                Logger.Warning("[Meteorite Research] Could not confirm one-level purchasing; no purchase attempted.");
                yield break;
            }

            var cost = research.levelUpCost.GetDecrypted();
            var before = Balance();
            var level = research.level;
            if (!double.IsFinite(before) || before < 0 || !double.IsFinite(cost) || cost <= 0 ||
                _keep.Value < 0 || level < 0)
            {
                Logger.Warning("[Meteorite Research] Balance, cost, reserve or level unreadable; no purchase attempted.");
                yield break;
            }
            if (!PurchaseObservation.CanAffordWithReserve(before, cost, _keep.Value))
            {
                Logger.Info($"[Meteorite Research] Keeping reserve: have {before}, cost {cost}, keep {_keep.Value}.");
                yield break;
            }
            var clicked = false;
            yield return Ui.ClickIf(Buy, () =>
            {
                before = Balance();
                return Selected(research) && SingleQuantity &&
                       research.level == level && research.IsUnlocked() && !research.IsMaxLevel() &&
                       research.levelUpCost.GetDecrypted() == cost &&
                       PurchaseObservation.CanAffordWithReserve(before, cost, _keep.Value);
            }, value => clicked = value, 1.2f, $"meteorite research {name}");
            if (!clicked)
            {
                Logger.Warning("[Meteorite Research] Balance, price, reserve or selection changed; purchase stopped.");
                result(PriorityTierAction.RetrySoon);
                yield break;
            }

            var confirmed = false;
            yield return Ui.WaitUntil(() =>
                    research.level == level + 1 && double.IsFinite(Balance()) && Balance() == before - cost,
                value => confirmed = value, 8f);
            if (!confirmed || Ui.MissingCurrency)
            {
                Logger.Warning($"[Meteorite Research] {name}: level and exact debit not confirmed; no further purchases.");
                yield break;
            }
            Logger.Info($"[Meteorite Research] {name}: spent {cost}, balance {Balance()}, reserve {_keep.Value}.");
        }
        result(research.IsMaxLevel() ? PriorityTierAction.Advance : PriorityTierAction.RetrySoon);
    }
}
