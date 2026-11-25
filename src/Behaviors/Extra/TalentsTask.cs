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
    public override GameFeature RequiredFeature => GameFeature.CharacterTalents;

    protected override string NotificationPath => P.Notifications + "/TalentAvailable";
    protected override bool EnabledByDefault => true;
    protected override bool UseScheduledTime => true;

    private const string C = P.Popups + "/Character/bg";
    private const string Tree = C + "/submenus/talents/bg/talentTreeScroll/Viewport/content/talentTree";
    private const string Points = C + "/submenus/talents/bg/talentPoints/container/pointInfo/talentsTextBg/talentsText";
    private const string Preview = P.Popups + "/TalentPreview/bg";
    private MelonPreferences_Entry<string> _priority;
    private MelonPreferences_Entry<string> _plan;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority", RecommendedOrders.Talents,
            "Priority", "Talent names to fill first, comma-separated. Others are filled after, in tree order.");
        RecommendedOrders.Adopt(c, _priority);
        _plan = c.CreateEntry("plan", TalentPlan.Guide, "Guide plan",
            "Staged talent plan followed step by step before the priority list. Steps are Name#copy:level, " +
            "comma-separated; #2 is the second copy of a repeated talent counting from the top tier. " +
            "Leave empty to use only the priority list.");
    }

    private sealed class NodeInfo
    {
        public Transform Node;
        public string Name;
        public double Level;
        public int Cap;
        public bool Unlocked;
        public int Order;
    }

    private static double Free()
    {
        var text = Ui.Text(Points) ?? "";
        var free = text.Contains('/') ? Ui.Parse(text.Split('/')[0]) : -1;
        return double.IsFinite(free) && free >= 0 && Math.Truncate(free) == free ? free : -1;
    }

    private static bool ReadNode(Transform node, out string name, out bool available) =>
        ReadNode(node, out name, out available, out _, out _, out _);

    private static bool ReadNode(Transform node, out string name, out bool available, out double level,
        out int cap, out bool unlocked)
    {
        name = null;
        available = false;
        level = 0;
        cap = 0;
        unlocked = false;
        try
        {
#if MONO
            var talent = node?.GetComponent<global::TalentsInteraction>()?.talent;
#else
            var talent = node?.GetComponent<Il2Cpp.TalentsInteraction>()?.talent;
#endif
            if (talent == null || string.IsNullOrWhiteSpace(talent.name)) return false;
            level = talent.tempLevel;
            cap = talent.levelCap;
            if (!double.IsFinite(level) || level < 0 || cap <= 0) return false;
            name = Il2CppHolydayStudios.Translator.Translate(talent.name)?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name == talent.name && name.Contains('/')) return false;
            unlocked = talent.unlocked;
            available = unlocked && level < cap;
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
        var planHolds = false;
        if (Ui.Visible(Tree) && Free() > 0 && !string.IsNullOrWhiteSpace(_plan.Value))
        {
            yield return FollowPlan(() => spent++, hold => planHolds = hold);
        }
        if (!planHolds && Ui.Visible(Tree) && Free() > 0)
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

    /// <summary>
    ///     Walks the guide plan in order. Reports hold=true while the plan is unfinished, so no points leak into
    ///     the priority list ahead of the guide; hold=false once every step is done.
    /// </summary>
    private IEnumerator FollowPlan(Action add, Action<bool> hold)
    {
        hold(true);
        if (!TalentPlan.TryParse(_plan.Value, out var steps, out var error))
        {
            Logger.Warning($"[Talents] Guide plan ignored: {error}. Fix it in F1 or clear it; no points spent.");
            yield break;
        }

        var nodes = new List<NodeInfo>();
        foreach (var node in Ui.VisibleChildren(Tree).Where(t => t.name.StartsWith("talentInteraction")))
        {
            if (!ReadNode(node, out var name, out _, out var level, out var cap, out var unlocked))
            {
                Logger.Warning("[Talents] A talent's live data is unreadable; the guide plan was left untouched.");
                yield break;
            }
            nodes.Add(new NodeInfo
            {
                Node = node, Name = name, Level = level, Cap = cap, Unlocked = unlocked,
                Order = TalentPlan.NodeOrder(node.name, node.GetSiblingIndex())
            });
        }

        var treeNames = nodes.Select(n => n.Name).ToList();
        string TreeName(string planName) => TalentPlan.Resolve(planName, treeNames);
        List<NodeInfo> CopiesOf(string name) =>
            nodes.Where(n => TalentPlan.SameName(n.Name, name)).OrderBy(n => n.Order).ToList();
        // Locked talents are drawn in the tree too; if none are, unopened tiers may simply be hidden.
        var lockedVisible = nodes.Any(n => !n.Unlocked);
        var namesLogged = false;

        foreach (var planName in TalentPlan.Names(steps))
        {
            var name = TreeName(planName);
            if (name == null) continue;
            var copies = CopiesOf(name);
            if (!TalentPlan.CopiesConsistent(copies.Select(n => n.Unlocked).ToList()))
            {
                Logger.Warning($"[Talents] Copies of {name} unlock out of tree order; guide plan paused, no points spent.");
                yield break;
            }
        }

        const int MaxFillerPoints = 5;
        var fillerUsed = 0;

        void Refresh()
        {
            // A spent point can unlock the next tier, so re-read levels and unlock states.
            foreach (var n in nodes)
                if (ReadNode(n.Node, out _, out _, out var lvl, out _, out var open))
                {
                    n.Level = lvl;
                    n.Unlocked = open;
                }
        }

        NodeInfo FillerFor(int stepIndex)
        {
            for (var i = stepIndex - 1; i >= 0; i--)
            {
                var name = TreeName(steps[i].Name);
                if (name == null) continue;
                var copies = CopiesOf(name);
                if (copies.Count < steps[i].Copy) continue;
                var candidate = copies[steps[i].Copy - 1];
                if (candidate.Unlocked && (candidate.Cap <= 0 || candidate.Level < candidate.Cap)) return candidate;
            }
            return null;
        }

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var resolved = TreeName(step.Name);
            if (resolved == null && lockedVisible)
            {
                if (!namesLogged)
                {
                    Logger.Warning("[Talents] Talent names in the tree: " +
                                   string.Join(", ", treeNames.Distinct(StringComparer.OrdinalIgnoreCase)));
                    namesLogged = true;
                }
                Logger.Warning($"[Talents] Guide step {index + 1} ({TalentPlan.Describe(step)}): no talent named " +
                               $"'{step.Name}' in the tree; skipping this step. Fix the name in the Guide plan setting.");
                continue;
            }
            var copies = resolved == null ? new List<NodeInfo>() : CopiesOf(resolved);
            if (copies.Count < step.Copy)
            {
                Logger.Warning($"[Talents] Guide step {index + 1} ({TalentPlan.Describe(step)}): " +
                               $"only {copies.Count} cop(ies) of {step.Name} visible yet; holding.");
                yield break;
            }
            var info = copies[step.Copy - 1];
            var goal = TalentPlan.Goal(step, info.Cap);
            if (info.Level >= goal) continue;
            if (Free() <= 0) yield break;
            if (!info.Unlocked)
            {
                // Tiers open on total points spent. A guide that is a point or two short (an off-by-one in its
                // level ranges) would hold forever, so top up the latest earlier guide talent that is not maxed.
                var filler = fillerUsed < MaxFillerPoints ? FillerFor(index) : null;
                if (filler == null)
                {
                    Logger.Info($"[Talents] Guide step {index + 1} ({TalentPlan.Describe(step)}) is still locked; " +
                                "holding points until it opens.");
                    yield break;
                }
                Logger.Info($"[Talents] Guide step {index + 1} ({TalentPlan.Describe(step)}) is locked; adding a point " +
                            $"to {filler.Name} to open the next tier.");
                var filled = false;
                yield return LevelNode(filler, filler.Name, (int)filler.Level + 1, "Filler", add, ok => filled = ok);
                if (!filled) yield break;
                fillerUsed++;
                Refresh();
                index--;
                continue;
            }

            var reached = false;
            yield return LevelNode(info, resolved, goal, $"Guide step {index + 1}/{steps.Count} {TalentPlan.Describe(step)}",
                add, ok => reached = ok);
            if (!reached) yield break;
            Refresh();
        }

        Logger.Info("[Talents] Guide plan complete; remaining points follow the priority list.");
        hold(false);
    }

    /// <summary>Opens one talent's preview and buys levels until it reaches goal. Reports whether it got there.</summary>
    private IEnumerator LevelNode(NodeInfo info, string resolved, int goal, string label, Action add, Action<bool> ok)
    {
        ok(false);
        if (Ui.Visible(Preview))
        {
            Logger.Warning("[Talents] A previous preview is still open; guide plan paused.");
            yield break;
        }

        var node = info.Node;
        bool Same() => ReadNode(node, out var n, out var avail) && avail && TalentPlan.SameName(n, resolved);
        var clicked = false;
        yield return Ui.ClickIf(node, () => Same() && Free() > 0, value => clicked = value, 0.6f);
        if (!clicked || !Ui.Visible(Preview))
        {
            Logger.Warning($"[Talents] {resolved}: selection changed or preview did not open; stopping.");
            yield break;
        }
        var actualName = Ui.Text(Preview + "/talentName");
        if (!TalentPlan.SameName(resolved, actualName))
        {
            Logger.Warning($"[Talents] Expected {resolved}, found '{actualName}'; no points spent.");
            yield return Ui.Click(Preview + "/closeButton", 0.3f);
            NextRunTime = DateTime.Now.AddMinutes(2);
            BubbleQuietUntil = NextRunTime;
            yield break;
        }

        var before = info.Level;
        bool Reached() => ReadNode(node, out _, out _, out var lvl, out _, out _) && lvl >= goal;
        var action = PriorityTierAction.Advance;
        yield return Upgrade(resolved, add, value => action = value, Reached);
        if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.3f);
        if (ReadNode(node, out _, out _, out var after, out _, out var nowUnlocked))
        {
            info.Level = after;
            info.Unlocked = nowUnlocked;
        }
        Logger.Info($"[Talents] {label}: {resolved} level {before} → {info.Level}.");
        if (info.Level < goal)
        {
            if (action == PriorityTierAction.RetrySoon)
            {
                NextRunTime = DateTime.Now.AddMinutes(2);
                BubbleQuietUntil = NextRunTime;
            }
            yield break;
        }
        ok(true);
    }

    private static IEnumerator Upgrade(string name, Action add, Action<PriorityTierAction> result,
        Func<bool> reached = null)
    {
        for (var i = 0; i < 60 && Free() > 0; i++)
        {
            if (reached != null && reached())
            {
                result(PriorityTierAction.Advance);
                yield break;
            }
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

        if (reached != null && reached())
        {
            result(PriorityTierAction.Advance);
            yield break;
        }
        var free = Free();
        if (free < 0) Logger.Warning("[Talents] Available points became unreadable; lower tiers were left untouched.");
        result(free < 0 ? PriorityTierAction.Wait
            : free > 0 && Ui.Clickable(Preview + "/talentUpgrades/upgradeTalentButton")
                ? PriorityTierAction.RetrySoon : PriorityTierAction.Advance);
    }
}
