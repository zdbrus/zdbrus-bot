using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebot.Core;
using Firebot.Core.Tasks;
using MelonLoader;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

public class TalentsTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/TalentAvailable";
    protected override bool EnabledByDefault => true;
    protected override bool UseScheduledTime => true;

    private const string C = P.Popups + "/Character/bg";
    private const string Tree = C + "/submenus/talents/bg/talentTreeScroll/Viewport/content/talentTree";
    private const string Points = C + "/submenus/talents/bg/talentPoints/container/pointInfo/talentsTextBg/talentsText";
    private const string Preview = P.Popups + "/TalentPreview/bg";
    private MelonPreferences_Entry<string> _priority;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority", "All main attributes,Attribute damage,Attribute health,Attribute armor",
            "Priority", "Talent names to fill first, comma-separated. Others are filled after, in tree order.");
    }

    private static double Free()
    {
        var text = Ui.Text(Points) ?? "";
        var free = text.Contains('/') ? Ui.Parse(text.Split('/')[0]) : -1;
        return double.IsFinite(free) && free >= 0 && Math.Truncate(free) == free ? free : -1;
    }

    private static bool ReadNode(Transform node, out string name, out bool available)
    {
        name = null;
        available = false;
        try
        {
            var talent = node?.GetComponent<Il2Cpp.TalentsInteraction>()?.talent;
            if (talent == null || string.IsNullOrWhiteSpace(talent.name)) return false;
            double level = talent.tempLevel;
            int cap = talent.levelCap;
            if (!double.IsFinite(level) || level < 0 || cap <= 0) return false;
            name = Il2CppHolydayStudios.Translator.Translate(talent.name)?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name == talent.name && name.Contains('/')) return false;
            available = talent.unlocked && level < cap;
            return true;
        }
        catch (Exception e)
        {
            Logger.Warning($"[Talents] Could not read a talent: {e.GetType().Name} - {e.Message}");
            return false;
        }
    }

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(30);
        NextRunTime = DateTime.Now.AddHours(6);
        if (!Ui.Visible(C)) yield return Ui.Click(P.Battle + "/topLeftSideUI/playerAvatar", 1.5f);
        if (!Ui.Visible(C)) yield break;
        yield return Ui.Click(C + "/submenuButtons/talents", 1.5f);

        var spent = 0;
        if (Ui.Visible(Tree) && Free() < 0)
            Logger.Warning("[Talents] Available points are unreadable; points were left unspent.");
        if (Ui.Visible(Tree) && Free() > 0)
        {
            var order = (_priority.Value ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length > 0).ToList();
            var candidates = new List<(Transform node, string name)>();
            var unreadable = false;
            foreach (var node in Ui.VisibleChildren(Tree).Where(t => t.name.StartsWith("talentInteraction")))
            {
                if (!ReadNode(node, out var name, out var available))
                {
                    Logger.Warning("[Talents] A talent's live data is unreadable; priorities were left untouched.");
                    unreadable = true;
                    break;
                }
                if (available && Ui.Clickable(node)) candidates.Add((node, name));
            }

            Logger.Info($"[Talents] Found {candidates.Count} available talent(s) without opening previews.");
            foreach (var match in candidates.OrderBy(x => PriorityTier.Rank(order, x.name)))
            {
                if (unreadable || Free() <= 0) break;
                if (Ui.Visible(Preview))
                {
                    Logger.Warning("[Talents] A previous preview is still open; lower tiers were left untouched.");
                    break;
                }
                var clicked = false;
                yield return Ui.ClickIf(match.node,
                    () => ReadNode(match.node, out var name, out var available) && available &&
                          name == match.name && Free() > 0,
                    value => clicked = value, 0.6f);
                if (!clicked || !Ui.Visible(Preview))
                {
                    Logger.Warning($"[Talents] {match.name}: selection changed or preview did not open; stopping.");
                    break;
                }
                var actualName = Ui.Text(Preview + "/talentName");
                if (!string.Equals(match.name, actualName?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warning($"[Talents] Expected {match.name}, found '{actualName}'; no points spent.");
                    yield return Ui.Click(Preview + "/closeButton", 0.3f);
                    NextRunTime = DateTime.Now.AddMinutes(2);
                    BubbleQuietUntil = NextRunTime;
                    break;
                }

                var action = PriorityTierAction.Advance;
                yield return Upgrade(match.name, () => spent++, value => action = value);
                if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.3f);
                if (action == PriorityTierAction.Advance) continue;
                if (action == PriorityTierAction.RetrySoon)
                {
                    NextRunTime = DateTime.Now.AddMinutes(2);
                    BubbleQuietUntil = NextRunTime;
                }
                Logger.Info($"[Talents] Holding at {match.name}; lower tiers stay untouched.");
                break;
            }
        }

        Logger.Info($"[Talents] spent {spent} point(s), {Ui.Text(Points)} left");
        if (Ui.Visible(C)) yield return Ui.Click(C + "/closeButton", 1f);
        const string unsaved = P.Popups + "/UnsavedChanges/bg";
        if (Ui.Visible(unsaved))
        {
            yield return Ui.Click(unsaved + "/saveChangesButton", 1f);
            if (Ui.Visible(C)) yield return Ui.Click(C + "/closeButton", 1f);
        }
    }

    private static IEnumerator Upgrade(string name, Action add, Action<PriorityTierAction> result)
    {
        for (var i = 0; i < 60 && Free() > 0; i++)
        {
            var up = Ui.Find(Preview + "/talentUpgrades/upgradeTalentButton");
            var before = Ui.Text(Preview + "/talentLevelText");
            if (string.IsNullOrWhiteSpace(before) || up == null)
            {
                Logger.Warning($"[Talents] {name}: level or upgrade control unreadable; no purchase attempted.");
                result(PriorityTierAction.Wait);
                yield break;
            }
            if (PurchaseObservation.IsMaxed(before) || !Ui.Clickable(up))
            {
                result(PriorityTierAction.Advance);
                yield break;
            }
            var changed = false;
            yield return Ui.ClickUntil(up,
                () => PurchaseObservation.LevelIncreased(before, Ui.Text(Preview + "/talentLevelText")),
                value => changed = value, $"talent {name}", 0.4f, 2f);
            if (Ui.MissingCurrency || !changed)
            {
                result(PriorityTierAction.Wait);
                yield break;
            }
            add();
        }

        var free = Free();
        if (free < 0) Logger.Warning("[Talents] Available points became unreadable; lower tiers were left untouched.");
        result(free < 0 ? PriorityTierAction.Wait
            : free > 0 && Ui.Clickable(Preview + "/talentUpgrades/upgradeTalentButton")
                ? PriorityTierAction.RetrySoon : PriorityTierAction.Advance);
    }
}
