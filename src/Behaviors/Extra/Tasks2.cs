using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using Firebot.Core;
using Firebot.Core.Tasks;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

/// <summary>
///     Shared "upgrade tree" buyer: opens each node's preview, reads name / level / cost, then fills each
///     priority tier completely before moving to the next tier.
/// </summary>
internal static class NodeBuyer
{
    public sealed class Layout
    {
        public string Preview;   // popup root holding the texts and buttons
        public string Name;      // relative to Preview
        public string Level;     // "Level 2/5"
        public string Buy;       // buy button, relative to Preview
        public string Cost;      // cost text, relative to Preview
        public string Close;     // close button, relative to Preview
    }

    private sealed class NodeInfo
    {
        public bool Maxed;
        public double Cost;
        public string Name;
        public DateTime At;
    }

    private static readonly Dictionary<string, NodeInfo> Known = new();

    /// <summary>
    ///     True when a node's earlier preview already shows it cannot be bought now: maxed, a known cost above
    ///     the budget, or locked recently. Skips opening its preview again.
    /// </summary>
    internal static bool CanSkip(Transform node, double budget, bool retryUnaffordableAfterCooldown = true,
        string scope = "")
    {
        if (!Known.TryGetValue(UpgradeNames.KeyOf(node, scope), out var k)) return false;
        if (k.Maxed) return true;
        var age = DateTime.Now - k.At;
        if (k.Cost > 0)
            return budget < k.Cost && (!retryUnaffordableAfterCooldown || age < TimeSpan.FromHours(6));
        return age < TimeSpan.FromHours(2);
    }

    internal static void Remember(Transform node, bool maxed, double cost, string name = null, string scope = "")
    {
        var key = UpgradeNames.KeyOf(node, scope);
        if (name == null && Known.TryGetValue(key, out var previous)) name = previous.Name;
        Known[key] = new NodeInfo { Maxed = maxed, Cost = cost, Name = name, At = DateTime.Now };
        UpgradeNames.Remember(node, scope, name);
    }

    internal static void Forget(Transform node, string scope)
        => Known.Remove(UpgradeNames.KeyOf(node, scope));

    internal static bool TryGetKnown(Transform node, out bool maxed, out double cost, out string name,
        string scope = "")
    {
        if (Known.TryGetValue(UpgradeNames.KeyOf(node, scope), out var info))
        {
            maxed = info.Maxed;
            cost = info.Cost;
            name = info.Name;
            return true;
        }

        maxed = false;
        cost = 0;
        name = null;
        return false;
    }

    private static IEnumerator BuyOne(Layout layout, string name, Func<double> currency, double keep,
        Action<bool> result)
    {
        var levelPath = layout.Preview + "/" + layout.Level;
        var costPath = layout.Preview + "/" + layout.Cost;
        var buyPath = layout.Preview + "/" + layout.Buy;
        var levelBefore = Ui.Text(levelPath);
        var costBefore = Ui.NumberAt(costPath);
        var currencyBefore = currency();
        yield return Ui.ClickUntil(Ui.Find(buyPath),
            () => PurchaseObservation.LevelIncreased(levelBefore, Ui.Text(levelPath)) ||
                  PurchaseObservation.ChangedPrice(costBefore, Ui.NumberAt(costPath)) ||
                  PurchaseObservation.Decreased(currencyBefore, currency()),
            result, name, 1.2f);
    }

    internal static bool IsMaxed(string level) => PurchaseObservation.IsMaxed(level);

    public static IEnumerator Run(List<Transform> nodes, Layout l, Func<double> currency, string priority,
        double keep = 0, int maxPerNode = 10, string scope = "", Action retrySoon = null)
    {
        if (currency() - keep <= 0)
        {
            Logger.Info($"[Nodes] Skipping upgrade previews: currency {currency()}, reserve {keep}.");
            yield break;
        }

        var info = new List<(Transform node, string name, double cost, bool open)>();
        var skipped = 0;
        foreach (var node in nodes)
        {
            if (CanSkip(node, currency() - keep, scope: scope))
            {
                if (TryGetKnown(node, out var knownMaxed, out var knownCost, out var knownName, scope) &&
                    !knownMaxed && knownCost > 0 && !string.IsNullOrEmpty(knownName))
                    info.Add((node, knownName, knownCost, true));
                skipped++;
                continue;
            }

            if (TryGetKnown(node, out var cachedMaxed, out var cachedCost, out var cachedName, scope) &&
                !cachedMaxed && cachedCost > 0 && !string.IsNullOrEmpty(cachedName))
            {
                info.Add((node, cachedName, cachedCost, true));
                continue;
            }

            if (UpgradeNames.TryGet(node, scope, out var rememberedName))
            {
                info.Add((node, rememberedName, 0, true));
                continue;
            }

            if (!Ui.Clickable(node)) continue;
            if (Ui.Visible(l.Preview))
            {
                Logger.Warning("[Nodes] A previous preview is still open; lower tiers were left untouched.");
                UpgradeNames.Flush();
                yield break;
            }
            yield return Ui.Click(node, 0.9f);
            if (!Ui.Visible(l.Preview))
            {
                Logger.Warning("[Nodes] An unknown upgrade preview did not open; lower tiers were left untouched.");
                UpgradeNames.Flush();
                yield break;
            }
            var name = Ui.Text(l.Preview + "/" + l.Name).Trim();
            if (string.IsNullOrEmpty(name))
            {
                Logger.Warning("[Nodes] An upgrade name was unreadable; lower tiers were left untouched.");
                if (Ui.Visible(l.Preview)) yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
                UpgradeNames.Flush();
                yield break;
            }
            var level = Ui.Text(l.Preview + "/" + l.Level);
            var maxed = IsMaxed(level);
            var buy = l.Preview + "/" + l.Buy;
            var cost = Ui.NumberAt(l.Preview + "/" + l.Cost);
            var open = !maxed && Ui.Visible(buy) && cost > 0;
            Remember(node, maxed, open ? cost : 0, name, scope);
            if (!maxed && Ui.Visible(buy) && cost <= 0)
            {
                Logger.Warning($"[Nodes] {name}: price unreadable; lower tiers were left untouched.");
                if (Ui.Visible(l.Preview)) yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
                UpgradeNames.Flush();
                yield break;
            }

            info.Add((node, name, cost, open));
            Logger.Debug($"[Nodes] {name} | {level} | cost {cost} | buy {(Ui.Visible(buy) ? (Ui.Clickable(buy) ? "on" : "off") : "hidden")}");
            yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
        }
        UpgradeNames.Flush();

        var order = (priority ?? string.Empty).Split(',').Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0).ToList();

        Logger.Info($"[Nodes] {info.Count(u => u.open)} of {info.Count} previewed upgradable " +
                    $"({skipped} skipped from earlier reads), currency {currency()}");
        foreach (var up in info.Where(u => u.open).OrderBy(u => PriorityTier.Rank(order, u.name)).ThenBy(u => u.cost).ToList())
        {
            if (!Ui.Clickable(up.node)) continue;
            if (Ui.Visible(l.Preview))
            {
                Logger.Warning("[Nodes] A previous preview is still open; lower tiers were left untouched.");
                yield break;
            }

            yield return Ui.Click(up.node, 0.9f);
            var buy = l.Preview + "/" + l.Buy;
            if (!Ui.Visible(l.Preview))
            {
                Logger.Warning($"[Nodes] {up.name}: preview did not open; lower tiers were left untouched.");
                yield break;
            }
            var actualName = Ui.Text(l.Preview + "/" + l.Name);
            if (!UpgradeNames.Matches(up.node, scope, up.name, actualName))
            {
                Forget(up.node, scope);
                UpgradeNames.Flush();
                Logger.Warning($"[Nodes] Expected {up.name}, found '{actualName}'; reordering before buying.");
                if (Ui.Visible(l.Preview)) yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
                retrySoon?.Invoke();
                yield break;
            }

            var purchases = 0;
            for (; purchases < maxPerNode; purchases++)
            {
                var cost = Ui.NumberAt(l.Preview + "/" + l.Cost);
                if (cost <= 0 || currency() - keep < cost || !Ui.Clickable(buy))
                {
                    Logger.Debug($"[Nodes] stop {up.name}: cost {cost}, have {currency()}, clickable {Ui.Clickable(buy)}");
                    break;
                }
                var changed = false;
                yield return BuyOne(l, up.name, currency, keep, value => changed = value);
                if (Ui.MissingCurrency) break;
                if (!changed)
                {
                    Logger.Warning($"[Nodes] Stopping after {up.name} made no verified progress.");
                    if (Ui.Visible(l.Preview)) yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
                    yield break;
                }
            }

            if (!Ui.Visible(l.Preview))
            {
                Logger.Warning($"[Nodes] {up.name}: preview closed unexpectedly; lower tiers were left untouched.");
                yield break;
            }
            var finalLevel = Ui.Text(l.Preview + "/" + l.Level);
            var finalMaxed = IsMaxed(finalLevel);
            var finalCost = Ui.NumberAt(l.Preview + "/" + l.Cost);
            var available = Ui.Visible(buy);
            var missingCurrency = Ui.MissingCurrency;
            var action = !finalMaxed && Ui.Find(buy) == null
                ? PriorityTierAction.Wait
                : PriorityTier.AfterPass(finalMaxed, available, purchases >= maxPerNode,
                    Ui.Clickable(buy), finalCost, currency() - keep);
            Remember(up.node, finalMaxed, finalMaxed || !available ? 0 : finalCost, up.name, scope);
            if (Ui.Visible(l.Preview)) yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
            if (missingCurrency || action != PriorityTierAction.Advance)
            {
                if (action == PriorityTierAction.RetrySoon) retrySoon?.Invoke();
                Logger.Info($"[Nodes] Holding at {up.name}; lower tiers stay untouched" +
                            (action == PriorityTierAction.RetrySoon ? " until the next pass." : "."));
                yield break;
            }
        }
    }
}

/// <summary>Stops a task whose bubble stays lit (nothing affordable) from re-running every loop.</summary>
internal static class Cooldown
{
    private static readonly Dictionary<string, DateTime> Last = new();

    public static bool Recent(object task, int minutes)
    {
        var key = task.GetType().Name;
        if (Last.TryGetValue(key, out var t) && DateTime.Now - t < TimeSpan.FromMinutes(minutes)) return true;
        Last[key] = DateTime.Now;
        return false;
    }
}

public class ForbiddenKnowledgeTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.Guilds;

    protected override string NotificationPath => P.Notifications + "/ForbiddenKnowledge";
    protected override bool UseScheduledTime => true;

    private const string F = P.Menus + "/ForbiddenKnowledge";
    private MelonPreferences_Entry<string> _priority;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority", RecommendedOrders.ForbiddenKnowledge,
            "Priority", "Forbidden knowledge upgrades in buy order. Unlisted ones are bought after, cheapest first.");
        RecommendedOrders.Adopt(c, _priority);
    }

    private static double Tomes()
    {
        var counters = Ui.Find(F + "/counters");
        if (counters == null) return 0;
        for (var i = 0; i < counters.childCount; i++)
        {
            var c = counters.GetChild(i);
            if (c.gameObject.activeInHierarchy && c.name.Contains("TomeOfPower"))
                return Ui.NumberOf(c.Find("quantity"));
        }

        return 0;
    }

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(20);
        NextRunTime = DateTime.Now.AddHours(4);
        yield return Ui.OpenGuildBuilding("forbiddenKnowledge");
        yield return Ui.WaitFor(F);
        if (!Ui.Visible(F)) yield break;

        foreach (var god in new[] { "ledra", "yamanoth", "kramatak" })
        {
            var tab = F + "/submenuButtons/" + god;
            if (!Ui.Clickable(tab)) continue;
            yield return Ui.Click(tab, 1.2f);
            if (Tomes() <= 0) continue;

            var nodes = Ui.VisibleChildren(F + "/attributes")
                .Where(n => n.name.StartsWith("forbiddenKnoweledgeUpgrade")).ToList();
            yield return NodeBuyer.Run(nodes, new NodeBuyer.Layout
            {
                Preview = P.Popups + "/ForbiddenKnowledgePreview/bg",
                Name = "innerBg/upgradeName", Level = "innerBg/upgradeLevelText",
                Buy = "innerBg/normal/buyUpgradeButton", Cost = "innerBg/normal/buyUpgradeButton/layout/cost",
                Close = "closeButton"
            }, Tomes, _priority.Value, scope: "forbidden/" + god,
                retrySoon: () =>
                {
                    NextRunTime = DateTime.Now.AddMinutes(2);
                    BubbleQuietUntil = NextRunTime;
                });
        }

        yield return Ui.Click(F + "/closeButton");
    }
}

public class ScarabGameTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.ScarabGame;

    protected override string NotificationPath => P.Notifications + "/ScarabGame";
    protected override bool UseScheduledTime => true;

    private const string S = P.Menus + "/ScarabGame";
    private const string H = S + "/helpCanvas";
    private const string V = P.Menus + "/PharaohsVault";
    private const string Coins = V + "/counters/currencyInteraction (AncientCoin)/quantity";
    private MelonPreferences_Entry<bool> _play;
    private MelonPreferences_Entry<bool> _allowPharaoh;
    private MelonPreferences_Entry<bool> _vault;
    private ScarabAutoPlay _autoPlay;

    internal override void OnStopped()
    {
        _autoPlay?.Dispose();
        _autoPlay = null;
    }

    public override int MinimumLevel => LevelRequirements.ScarabGameShop;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _play = c.CreateEntry("play", true, "Play Scarab's Game", "Spins with Noble's tokens, or Pharaoh's tokens when explicitly enabled. Never buys tokens or spends gems.");
        _allowPharaoh = c.CreateEntry("allow_pharaoh_tokens", false, "Use Pharaoh's Tokens",
            "Also spend available Pharaoh's tokens on Scarab spins. Off by default; never buys more tokens.");
        _vault = c.CreateEntry("open_vault", true, "Open Pharaoh's Vault", "Opens the vault whenever you have enough ancient coins.");
    }

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(20);
        NextRunTime = DateTime.Now.AddHours(2);
        yield return Ui.OpenTownBuilding("tavern");
        yield return Ui.Click(P.Popups + "/TavernSelection/bg/scarabGame", 2.5f);
        if (!Ui.Visible(S)) yield break;

        if (_play.Value)
        {
            _autoPlay = new ScarabAutoPlay(() => IsEnabled && _play.Value, () => _allowPharaoh.Value);
            yield return _autoPlay.Execute();
            if (_autoPlay.ResumeSoon)
            {
                NextRunTime = DateTime.Now.AddMinutes(1);
                BubbleQuietUntil = NextRunTime;
            }
            OnStopped();
        }

        var dedicatedVaultTaskEnabled = BotManager.AllTasks
            .OfType<PharaohsVaultTask>()
            .Any(task => task.IsEnabled);
        if (_vault.Value && !dedicatedVaultTaskEnabled && Ui.Clickable(H + "/actionButtons/pharaohVault"))
        {
            yield return Ui.Click(H + "/actionButtons/pharaohVault", 1.5f);
            yield return PharaohsVaultTask.OpenAvailableVaults();
            if (Ui.Visible(V)) yield return Ui.Click(V + "/closeButton", 1f);
        }

        if (Ui.Visible(S)) yield return Ui.Click(S + "/closeButton");
    }

}

public class ScarabMilestonesTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.ScarabGame;

    protected override string NotificationPath => P.Notifications + "/ScarabGameMilestones";

    private const string S = P.Menus + "/ScarabGame";
    private const string V = P.Menus + "/PharaohsVault";
    private const string Popup = P.Popups + "/ScarabGameMileStones/bg";
    private const string List = Popup + "/rewardsList/Scroll View/Viewport/Content/milestones";

    public override int MinimumLevel => LevelRequirements.ScarabGameShop;

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(20);
        NextRunTime = DateTime.Now.AddHours(6);
        yield return Ui.OpenTownBuilding("tavern");
        yield return Ui.Click(P.Popups + "/TavernSelection/bg/scarabGame", 2.5f);
        yield return Ui.Click(S + "/helpCanvas/actionButtons/pharaohVault", 1.5f);
        yield return Ui.Click(V + "/rightSideUI/milestones", 1.5f);
        var list = Ui.Find(List);
        if (list != null)
        {
            var confirmed = 0;
            var unconfirmed = 0;
            for (var i = 0; i < list.childCount; i++)
            {
                var claim = list.GetChild(i).Find("rewardRoot/claimButton");
                if (!Ui.Clickable(claim)) continue;
                var rewardWasVisible = Ui.Visible(P.RewardsClose);
                var changed = false;
                yield return Ui.ClickUntil(claim,
                    () => !Ui.Clickable(claim) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                    value => changed = value, "Scarab milestone", 1.5f);
                if (!changed)
                {
                    unconfirmed++;
                    Logger.Warning("[Scarab] Milestone claim had no observable state change; stopping claims.");
                    break;
                }

                confirmed++;
                yield return Ui.CloseRewards();
            }

            Logger.Info($"[Scarab] Claim controls changed for {confirmed} milestone(s); " +
                        $"{unconfirmed} claim(s) unconfirmed. Reward receipt is not independently verified.");
        }

        if (Ui.Visible(Popup)) yield return Ui.Click(Popup + "/closeButton", 0.8f);
        if (Ui.Visible(V)) yield return Ui.Click(V + "/closeButton", 0.8f);
        if (Ui.Visible(S)) yield return Ui.Click(S + "/closeButton");
    }
}

/// <summary>
///     Handles the reward bubbles that have no dedicated task yet: clicks the bubble, presses only free
///     claim/collect buttons in whatever opens (never anything with a price), then closes it. Each opened screen is
///     also saved by the screen mapper so a full task can be written for it later.
/// </summary>
public class BubbleRewardsTask : BotTask
{
    private const string PreviousDefaultBubbles =
        "SignIn,LockerRewards,PiratesPrize,HallOfHeroes,TowerOfSouls,Prophet,Mercenaries,AncientArtifact," +
        "TalentAvailable,GuardianEvolution,GuardianRarity,WarMachinesRarity,WarmachineUnlocked," +
        "ScarabGameBeastRelease,UnlockLedra,UnlockYamanoth,UnlockKramatak,BattleLive";
    private const string DefaultBubbles = PreviousDefaultBubbles + ",ChaosRiftSupplies,GuardianHolyUpgrade";

    private static readonly Regex FreeButton =
        new(@"^(claim|collect|free|getfree|claimall|claimbutton|collectbutton|claimallbutton|collectall|purchasefreebutton)", RegexOptions.IgnoreCase);

    private MelonPreferences_Entry<string> _bubbles;
    // A bubble that opened nothing free is left alone for a while instead of being reopened every 10 minutes.
    private static readonly Dictionary<string, DateTime> Quiet = new();

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _bubbles = c.CreateEntry("bubbles", DefaultBubbles,
            "Bubbles", "Notification bubbles to open for free rewards (comma-separated bubble names).");
        if (_bubbles.Value == PreviousDefaultBubbles) _bubbles.Value = DefaultBubbles;
    }

    internal override IEnumerable<string> HandledNotificationNames => BubbleNames();

    protected override bool IsNotificationVisibleCore()
    {
        if (!IsEnabled || DateTime.Now < BubbleQuietUntil) return false;

        return BubbleNames().Any(name =>
            (!Quiet.TryGetValue(name, out var until) || DateTime.Now >= until) &&
            Ui.Visible(P.Notifications + "/" + name));
    }

    private IEnumerable<string> BubbleNames()
    {
        var ownedByEnabledTasks = BotManager.AllTasks
            .Where(task => task != this && task.IsEnabled)
            .SelectMany(task => task.HandledNotificationNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (_bubbles?.Value ?? string.Empty).Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && !ownedByEnabledTasks.Contains(s));
    }

    // Every lit bubble and who answers it, so one that nobody handles shows up in the log by name.
    private void LogLitBubbles()
    {
        try
        {
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in BotManager.AllTasks)
                if (task != this)
                    foreach (var n in task.HandledNotificationNames)
                        owners[n] = task.SectionTitle + (task.IsEnabled ? "" : " (disabled)");
            var mine = (_bubbles?.Value ?? string.Empty).Split(',').Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var lit = Ui.VisibleChildren(P.Notifications).Select(t => t.name).ToList();
            var parts = lit.Select(n => owners.TryGetValue(n, out var o) ? $"{n}->{o}"
                : mine.Contains(n) ? $"{n}->Bubble Rewards" : $"{n}->UNHANDLED");
            Logger.Info($"[Bubbles] Lit now ({lit.Count}): {string.Join(", ", parts)}");
        }
        catch (Exception e)
        {
            Logger.Debug($"[Bubbles] Could not list lit bubbles: {e.Message}");
        }
    }

    private static HashSet<int> OpenRoots()
    {
        var set = new HashSet<int>();
        foreach (var root in new[] { P.Menus, P.Popups, P.Events })
            foreach (var t in Ui.VisibleChildren(root))
                set.Add(t.GetInstanceID());
        return set;
    }

    private static HashSet<int> ActiveFreeClaimButtons()
    {
        var set = new HashSet<int>();
        foreach (var root in new[] { P.Menus, P.Popups, P.Events })
        {
            var container = Ui.Find(root);
            if (container == null) continue;

            foreach (var button in container.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if (button != null && button.enabled && button.interactable &&
                    button.gameObject.activeInHierarchy && FreeButton.IsMatch(button.name) &&
                    !HasPrice(button.transform))
                    set.Add(button.GetInstanceID());
        }

        return set;
    }

    private static UnityEngine.UI.Button FindNewFreeClaimButton(HashSet<int> existing, HashSet<int> attempted)
    {
        foreach (var root in new[] { P.Menus, P.Popups, P.Events })
        {
            var container = Ui.Find(root);
            if (container == null) continue;

            foreach (var button in container.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if (button != null && button.enabled && button.interactable &&
                    button.gameObject.activeInHierarchy && FreeButton.IsMatch(button.name) &&
                    !existing.Contains(button.GetInstanceID()) && !attempted.Contains(button.GetInstanceID()) &&
                    !HasPrice(button.transform))
                {
                    attempted.Add(button.GetInstanceID());
                    return button;
                }
        }

        return null;
    }

    private static bool HasPrice(Transform b)
    {
        foreach (var tmp in b.GetComponentsInChildren<Il2CppTMPro.TMP_Text>())
        {
            var s = tmp.text ?? "";
            if (s.Contains('$') || s.Contains('€') || s.Contains('£')) return true;
        }

        return false;
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddMinutes(10);
        LogLitBubbles();
        foreach (var name in BubbleNames())
        {
            var path = P.Notifications + "/" + name;
            if (!Ui.Visible(path)) continue;
            if (Quiet.TryGetValue(name, out var until) && DateTime.Now < until) continue;

            var before = OpenRoots();
            var buttonsBefore = ActiveFreeClaimButtons();
            yield return new Firebot.GameModel.Primitives.GameNotificationButton(path).Click();
            yield return new WaitForSeconds(2f);

            var opened = new List<Transform>();
            foreach (var root in new[] { P.Menus, P.Popups, P.Events })
                opened.AddRange(Ui.VisibleChildren(root).Where(t => !before.Contains(t.GetInstanceID())));

            var clicked = 0;
            var changed = 0;
            var attempted = new HashSet<int>();
            for (var pass = 0; pass < 10; pass++)
            {
                var button = FindNewFreeClaimButton(buttonsBefore, attempted);
                if (button == null) break;

                yield return Ui.Click(button.transform, 1.5f, $"{name}: {button.name}");
                clicked++;

                var deadline = Time.unscaledTime + 2f;
                while (Time.unscaledTime < deadline && button != null && button.gameObject.activeInHierarchy &&
                       button.enabled && button.interactable)
                    yield return new WaitForSeconds(0.2f);
                if (button == null || !button.gameObject.activeInHierarchy || !button.enabled || !button.interactable)
                    changed++;

                yield return Ui.CloseRewards();
            }

            Logger.Info($"[Bubbles] {name}: opened {(opened.Count == 0 ? "no new top-level screen" : string.Join(", ", opened.Select(o => o.name)))}; " +
                        $"clicked {clicked} free claim button(s); {changed} button(s) became unavailable afterward (reward not independently verified).");
            Quiet[name] = DateTime.Now.Add(clicked == 0 ? TimeSpan.FromHours(3) : TimeSpan.FromMinutes(1));

            // Close what the bubble opened, newest first.
            foreach (var screen in opened.AsEnumerable().Reverse())
            {
                if (!screen.gameObject.activeInHierarchy) continue;
                var close = screen.Find("closeButton") ?? screen.Find("bg/closeButton");
                if (close != null) yield return Ui.Click(close, 0.8f);
            }
        }
    }
}

public class PharaohsVaultTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.ScarabGame;

    protected override string NotificationPath => P.Notifications + "/PharaohsVault";

    private const string S = P.Menus + "/ScarabGame";
    private const string V = P.Menus + "/PharaohsVault";
    private const string Coins = V + "/counters/currencyInteraction (AncientCoin)/quantity";

    public override int MinimumLevel => LevelRequirements.ScarabGameShop;

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(20);
        NextRunTime = DateTime.Now.AddHours(6);
        yield return Ui.OpenTownBuilding("tavern");
        yield return Ui.Click(P.Popups + "/TavernSelection/bg/scarabGame", 2.5f);
        yield return Ui.Click(S + "/helpCanvas/actionButtons/pharaohVault", 1.5f);
        yield return OpenAvailableVaults();

        if (Ui.Visible(V)) yield return Ui.Click(V + "/closeButton", 0.8f);
        if (Ui.Visible(S)) yield return Ui.Click(S + "/closeButton");
    }

    internal static IEnumerator OpenAvailableVaults()
    {
        var opened = 0;
        for (var n = 0; n < 10 && Ui.Visible(V); n++)
        {
            var cost = Ui.NumberAt(V + "/openButton/layout/costText");
            var before = Ui.NumberAt(Coins);
            if (cost <= 0)
            {
                Logger.Warning("[Pharaoh's Vault] Could not read the opening cost; stopping.");
                break;
            }

            if (before < cost)
            {
                Logger.Info($"[Pharaoh's Vault] Need {cost} ancient coins; have {before}. No opening attempted.");
                break;
            }

            var confirmed = false;
            yield return Ui.ClickUntil(Ui.Find(V + "/openButton"),
                () => Ui.NumberAt(Coins) < before,
                value => confirmed = value, "Pharaoh's Vault", 4f, 3f);
            if (!confirmed)
            {
                Logger.Warning("[Pharaoh's Vault] Ancient coin balance did not decrease; stopping to avoid retrying.");
                break;
            }

            opened++;
            yield return Ui.CloseRewards();
            if (Ui.MissingCurrency) break;
        }

        Logger.Info($"[Pharaoh's Vault] Confirmed {opened} opening(s).");
    }
}

/// <summary>
///     Seasonal event exchange (e.g. Trick or Treat "Pumpkin exchange" in the Witch's hut): buys the items you list,
///     in order, with the event currency only, up to each item's limit.
/// </summary>
public class EventExchangeTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.EventManager;

    private MelonPreferences_Entry<string> _priority;
    private MelonPreferences_Entry<int> _keep;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority", RecommendedOrders.EventExchange,
            "Items To Buy", "Event exchange items to buy, in order (comma-separated). Items not listed are never bought.");
        RecommendedOrders.Adopt(c, _priority);
        _keep = c.CreateEntry("keep_currency", 0, "Event Currency To Keep", "Event currency to leave unspent.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.OpenTownBuilding("event");
        yield return new WaitForSeconds(1.5f);

        var shop = Ui.VisibleChildren(P.Events).FirstOrDefault(e => e.name.Contains("Shop"));
        if (shop == null)
        {
            Logger.Info("[EventExchange] No event shop open (no seasonal event right now?).");
            yield break;
        }

        // Open the exchange tab (the one whose name ends in "Exchange").
        var tabs = shop.Find("bg/submenuButtons");
        if (tabs != null)
            for (var i = 0; i < tabs.childCount; i++)
                if (tabs.GetChild(i).name.EndsWith("Exchange", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Ui.Click(tabs.GetChild(i), 1.2f);
                    break;
                }

        Transform counter = null;
        var counters = shop.Find("bg/counters");
        if (counters != null && counters.childCount > 0) counter = counters.GetChild(0).Find("quantity");
        double Have() => counter == null ? 0 : Ui.NumberOf(counter);

        var items = new List<Transform>();
        var subs = shop.Find("bg/submenus");
        if (subs != null)
            foreach (var sub in Ui.VisibleChildren(GetPath(subs)))
            {
                var content = sub.Find("scrollView/viewport/content");
                if (content == null) continue;
                for (var i = 0; i < content.childCount; i++)
                    if (content.GetChild(i).gameObject.activeInHierarchy) items.Add(content.GetChild(i));
            }

        var order = (_priority.Value ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();
        var purchased = 0;
        var stopBuying = false;
        foreach (var want in order)
        {
            var item = items.FirstOrDefault(it => Ui.Text(it.Find("itemName")).Trim().ToLowerInvariant() == want);
            if (item == null) continue;
            var buy = item.Find("purchaseButton");
            var itemPurchases = 0;
            for (; itemPurchases < 40; itemPurchases++)
            {
                var cost = Ui.NumberOf(buy?.Find("purchaseCost"));
                var m = Regex.Match(Ui.Text(item.Find("remainingQuantity")) ?? "", @"(\d+)\s*/\s*(\d+)");
                if (m.Success && int.Parse(m.Groups[1].Value) >= int.Parse(m.Groups[2].Value)) break;
                if (cost <= 0 || Have() - _keep.Value < cost || !Ui.Clickable(buy)) break;
                var before = Have();
                var confirmed = false;
                yield return Ui.ClickUntil(buy, () => PurchaseObservation.Decreased(before, Have()),
                    value => confirmed = value, "event exchange: " + want, 1.2f, 3f);
                if (!confirmed)
                {
                    Logger.Warning($"[EventExchange] {want}: event currency did not decrease; stopping purchases.");
                    stopBuying = true;
                    break;
                }

                purchased++;
                yield return Ui.CloseRewards();
                if (Ui.MissingCurrency) yield break;
            }

            if (stopBuying) break;
            var remaining = Regex.Match(Ui.Text(item.Find("remainingQuantity")) ?? "", @"(\d+)\s*/\s*(\d+)");
            var itemMaxed = remaining.Success && int.Parse(remaining.Groups[1].Value) >= int.Parse(remaining.Groups[2].Value);
            var nextCost = Ui.NumberOf(buy?.Find("purchaseCost"));
            var available = buy != null && buy.gameObject.activeInHierarchy;
            var action = PriorityTier.AfterPass(itemMaxed, available, itemPurchases >= 40,
                Ui.Clickable(buy), nextCost, Have() - _keep.Value);
            if (action != PriorityTierAction.Advance)
            {
                if (action == PriorityTierAction.RetrySoon) NextRunTime = DateTime.Now.AddMinutes(2);
                Logger.Info($"[EventExchange] Holding at {want}: cost {nextCost}, have {Have() - _keep.Value}. Lower tiers stay untouched.");
                break;
            }
        }

        var close = shop.Find("bg/closeButton");
        if (close != null) yield return Ui.Click(close, 0.8f);
        Logger.Info($"[EventExchange] Confirmed {purchased} purchase(s).");
    }

    private static string GetPath(Transform t)
    {
        var parts = new List<string>();
        for (var x = t; x != null; x = x.parent) parts.Insert(0, x.name);
        return string.Join("/", parts);
    }
}

/// <summary>Battle pass (Path of Glory): claims every free-track reward that is ready. Never touches the golden pass.</summary>
public class BattlePassTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.BattlePass;

    private const string B = P.Popups + "/BattlePass/bg";
    private const string Scroll = B + "/submenus/rewards/bg/scrollView";

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.Click(P.Battle + "/bottomSideUIDesktop/pathOfGloryButton", 2f);
        if (!Ui.Visible(B)) yield break;
        yield return Ui.Click(B + "/submenuButtons/rewards", 1.5f);

        var rewardNotification = B + "/submenuButtons/rewards/notification";
        var notificationFound = Ui.Find(rewardNotification) != null;
        if (!BattlePassPolicy.ShouldScanRewards(notificationFound, Ui.Visible(rewardNotification)))
        {
            Logger.Info("[BattlePass] No free rewards are ready; skipped scrolling the reward track.");
            yield return Ui.Click(B + "/closeButton");
            yield break;
        }

        var rect = Ui.Find(Scroll)?.GetComponent<UnityEngine.UI.ScrollRect>();
        var content = Ui.Find(Scroll + "/viewport/content/layout");
        var claimed = 0;
        var unconfirmed = 0;
        var stalled = false;
        for (var step = 0; step <= 20 && content != null && !stalled; step++)
        {
            if (rect != null) rect.horizontalNormalizedPosition = step / 20f;
            yield return new WaitForSeconds(0.4f);
            for (var i = 0; i < content.childCount; i++)
            {
                if (stalled) break;
                var claim = content.GetChild(i).Find("freeBg/glowOutlineFree/rewardRoot/claimButton");
                if (!Ui.Clickable(claim)) continue;
                var rewardWasVisible = Ui.Visible(P.RewardsClose);
                var confirmed = false;
                yield return Ui.ClickUntil(claim,
                    () => !Ui.Clickable(claim) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                    value => confirmed = value, "battle pass free reward", 1.2f);
                yield return Ui.CloseRewards();
                if (confirmed) claimed++;
                else
                {
                    unconfirmed++;
                    stalled = true;
                }
            }
        }

        Logger.Info($"[BattlePass] Free reward controls changed for {claimed} reward(s); {unconfirmed} claim(s) unconfirmed. " +
                    "Reward receipt is not independently verified.");
        yield return Ui.Click(B + "/closeButton");
    }
}

/// <summary>
///     Opens the chests in the Bag (gear, jewel and celestial chests). A chest is opened only when it costs nothing but
///     itself: the preview must show no requirement and the open button must carry the chest's own icon.
/// </summary>
public class OpenChestsTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.InventoryBag;

    private const string Bag = P.Battle + "/bottomSideUIDesktop/menuButtons/inventoryButtonUI";
    private const string Inv = P.Menus + "/Inventory";
    private const string InventoryTitle = Inv + "/titleText";
    private const string Content = Inv + "/submenus/items/ScrollView/Viewport/Content";
    private const string Preview = P.Popups + "/ChestOpenPreview/bg";
    private const string Opening = P.Menus + "/ChestOpening";
    private const string NewItems = P.Popups + "/NewItem";
    private const string MysteryBoxId = MysteryBoxOpener.ItemId;
    private MelonPreferences_Entry<bool> _openMysteryBoxes;
    // The game's own chest codes (GearChestCode, JewelChestCode, CelestialChestCode). A chest whose tile shows a
    // requirement (gear power, campaign stars, Oracle level) is kept by the requirement check below.
    private static readonly string[] ChestIds =
    {
        "Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic", "Titan", "Angel",
        "Wooden", "Iron", "Golden", "Diamond", "Opal", "Emerald", "Platinum", "Bloodstone",
        "Comet", "Lunar", "Solar", "Nebula", "Cosmic", "Galaxy", "Stellar", "Constellation"
    };
    private static readonly HashSet<string> SupportedChests = new(ChestIds, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MelonPreferences_Entry<bool>> _open = new(StringComparer.OrdinalIgnoreCase);
    protected override bool EnabledByDefault => true;

    // Keyed by the chest tile's object name in the game (its ID), never by the displayed text.
    protected override void OnConfigure(MelonPreferences_Category c)
    {
        foreach (var id in ChestIds)
            _open[id] = c.CreateEntry("open_" + id.ToLowerInvariant(), true, "Open " + id + " Chests",
                "Tick to let the bot open this chest type (game id: " + id + "). Untick to keep them.");
        _openMysteryBoxes = c.CreateEntry("open_mysterybox", true, "Open Mystery Boxes",
            "Tick to let the bot open the free Mystery Boxes in your Bag. Untick to keep them.");
        _keepForDaily = c.CreateEntry("keep_one_for_daily", true, "Keep One For Daily Quest",
            "Guide tip: leaves one chest unopened so tomorrow's \"open chests\" daily quest always has one.");
    }

    private MelonPreferences_Entry<bool> _keepForDaily;

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddMinutes(20);
        var skip = _open.Where(kv => !kv.Value.Value).Select(kv => kv.Key.ToLowerInvariant()).ToHashSet();
        yield return Ui.Click(Bag, 1.5f);
        yield return Ui.WaitFor(Inv);
        if (!Ui.Visible(Inv))
        {
            Logger.Warning("[Chests] Bag did not open; no chests were processed.");
            yield break;
        }

        if (!string.Equals(Ui.Text(InventoryTitle), "Chests", StringComparison.OrdinalIgnoreCase))
            yield return Ui.Click(Inv + "/submenuButtons/chests", 1.2f);
        yield return Ui.WaitFor(Content);
        if (!Ui.Visible(Content))
        {
            Logger.Warning("[Chests] Chest inventory did not appear; no chests were processed.");
            yield return Ui.Click(Inv + "/closeButton");
            yield break;
        }

        var opened = 0;
        var foundChest = false;
        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var chest = Ui.VisibleChildren(Content).FirstOrDefault(t =>
                SupportedChests.Contains(t.name) && !skip.Contains(t.name.ToLowerInvariant()) &&
                Ui.NumberOf(t.Find("quantity")) > 0 && Ui.Clickable(t));
            if (chest == null)
            {
                if (!foundChest) Logger.Info("[Chests] No eligible clickable chest found; none opened.");
                break;
            }

            foundChest = true;
            var name = chest.name;
            var beforeQuantity = Ui.NumberOf(chest.Find("quantity"));
            yield return Ui.Click(chest, 1.2f);
            yield return Ui.WaitFor(Preview, 2f);
            if (!Ui.Visible(Preview))
            {
                Logger.Info($"[Chests] Button click on {name} showed no preview; sending a pointer click.");
                Ui.PointerClick(chest);
                yield return Ui.WaitFor(Preview, 4f);
            }

            if (!Ui.Visible(Preview))
            {
                Logger.Warning($"[Chests] Clicking {name} did not open its preview; stopping.");
                break;
            }

            if (Ui.Visible(Preview + "/chestRequirementBg"))
            {
                Logger.Info($"[Chests] {name} has a requirement; keeping it.");
                skip.Add(name.ToLowerInvariant());
                kept[name] = "requirement";
                yield return Ui.Click(Preview + "/closeButton", 0.6f);
                continue;
            }

            // Keep one chest for the next daily quest: only the last eligible stack is touched, and it is opened
            // in steps that always leave at least one behind.
            var options = new[] { "openxMax", "openx10", "openx1" };
            if (_keepForDaily?.Value == true)
            {
                var others = Ui.VisibleChildren(Content).Count(t => t.name != name &&
                    SupportedChests.Contains(t.name) && !skip.Contains(t.name.ToLowerInvariant()) &&
                    Ui.NumberOf(t.Find("quantity")) > 0);
                if (others == 0)
                {
                    var plan = ChestKeep.Options(beforeQuantity);
                    if (plan.Length == 0)
                    {
                        Logger.Info($"[Chests] Keeping the last {name} chest for tomorrow's daily quest.");
                        skip.Add(name.ToLowerInvariant());
                        kept[name] = "saved for the daily quest";
                        yield return Ui.Click(Preview + "/closeButton", 0.6f);
                        continue;
                    }
                    options = plan;
                }
            }

            Transform open = null;
            for (var attempt = 0; attempt < 20 && open == null; attempt++)
            {
                foreach (var option in options)
                {
                    var b = Ui.Find(Preview + "/openingOptions/" + option);
                    if (Ui.Clickable(b) && b.Find("chestIcon") != null && b.Find("chestIcon").gameObject.activeInHierarchy)
                    {
                        open = b;
                        break;
                    }
                }

                if (open == null) yield return new WaitForSeconds(0.25f);
            }

            if (open == null)
            {
                Logger.Info($"[Chests] {name} has no safe free opening option; keeping it.");
                skip.Add(name.ToLowerInvariant());
                kept[name] = "no safe free opening option";
                yield return Ui.Click(Preview + "/closeButton", 0.6f);
                continue;
            }

            var quantity = Ui.Text(open.Find("quantity"));
            yield return Ui.Click(open, 2f, $"request opening {name} chest {quantity}");
            if (Ui.MissingCurrency) break;
            yield return Ui.WaitFor(Opening);
            if (!Ui.Visible(Opening))
            {
                Logger.Warning($"[Chests] The {name} open request did not show a reward screen; stopping.");
                break;
            }

            Logger.Info($"[Chests] Reward screen appeared for {name} chest selection {quantity}.");

            // Skip the animation, then close the reward screen.
            for (var w = 0; w < 30 && Ui.Visible(Opening) && !Ui.Clickable(Opening + "/closeButton"); w++)
            {
                if (Ui.Clickable(Opening + "/background")) yield return Ui.Click(Opening + "/background", 0.5f);
                else yield return new WaitForSeconds(0.5f);
            }

            if (Ui.Clickable(Opening + "/closeButton")) yield return Ui.Click(Opening + "/closeButton", 1f);
            if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.6f);

            // Gear drops raise a "New items" popup whose Equip button is also its close button.
            for (var n = 0; n < 5 && Ui.Visible(NewItems); n++)
            {
                Logger.Info($"[Chests] Closing the New items popup after {name} chests.");
                yield return Ui.Click(NewItems + "/bg/closeButton", 1f);
            }

            var updateDeadline = Time.unscaledTime + 5f;
            while (Time.unscaledTime < updateDeadline)
            {
                var current = Ui.Find(Content + "/" + name);
                if (current == null || !current.gameObject.activeInHierarchy ||
                    Ui.NumberOf(current.Find("quantity")) < beforeQuantity) break;
                yield return new WaitForSeconds(0.25f);
            }

            var updatedChest = Ui.Find(Content + "/" + name);
            var afterQuantity = updatedChest != null && updatedChest.gameObject.activeInHierarchy
                ? Ui.NumberOf(updatedChest.Find("quantity"))
                : 0;
            if (afterQuantity >= beforeQuantity)
            {
                Logger.Warning($"[Chests] {name} quantity did not decrease after its reward screen; stopping to avoid retrying.");
                break;
            }

            opened++;
            Logger.Info($"[Chests] Confirmed {name} quantity decreased from {beforeQuantity} to {afterQuantity}.");
        }

        var remaining = Ui.VisibleChildren(Content)
            .Where(t => SupportedChests.Contains(t.name) && !skip.Contains(t.name.ToLowerInvariant()) &&
                        Ui.NumberOf(t.Find("quantity")) > 0)
            .ToList();
        var owned = Ui.VisibleChildren(Content)
            .Where(t => Ui.NumberOf(t.Find("quantity")) > 0)
            .ToList();
        if (remaining.Count > 0)
            Logger.Warning($"[Chests] Stopped with eligible supported chests remaining: {string.Join(", ", remaining.Select(t => $"{t.name} x{Ui.NumberOf(t.Find("quantity"))}"))}.");
        else
            Logger.Info("[Chests] Verified no eligible supported chests remain.");
        if (kept.Count > 0)
            Logger.Info($"[Chests] Kept chests: {string.Join(", ", kept.Select(kv => $"{kv.Key} ({kv.Value})"))}.");
        var configuredSkips = owned.Where(t =>
                (skip.Contains(t.name.ToLowerInvariant()) && !kept.ContainsKey(t.name)) ||
                (_openMysteryBoxes?.Value == false && t.name == MysteryBoxId))
            .Select(t => $"{t.name} x{Ui.NumberOf(t.Find("quantity"))}").ToList();
        if (configuredSkips.Count > 0)
            Logger.Info($"[Chests] Left unopened by configured keep list: {string.Join(", ", configuredSkips)}.");
        var unsupported = owned.Where(t => !SupportedChests.Contains(t.name) && t.name != MysteryBoxId)
            .Select(t => $"{t.name} x{Ui.NumberOf(t.Find("quantity"))}").ToList();
        if (unsupported.Count > 0)
            Logger.Info($"[Chests] Left unopened because the chest type is unsupported: {string.Join(", ", unsupported)}.");

        if (_openMysteryBoxes?.Value == true && Ui.Visible(Content)) yield return MysteryBoxOpener.Open(Content);

        if (Ui.Visible(Inv)) yield return Ui.Click(Inv + "/closeButton");
        Logger.Info($"[Chests] Confirmed {opened} chest opening(s).");
    }

}

/// <summary>
///     After a lost boss fight the game keeps farming the stage until someone presses "Boss". This presses it again
///     every few minutes, once the heroes have had time to level. Losing costs nothing.
///     <para>
///         With Stage Fallback on (issue #6) a lost boss, or too long at one stage, steps back a few stages to farm
///         where the team wins, and the next try comes after "Minutes Between Tries".
///     </para>
/// </summary>
public class BossRetryTask : BotTask
{
    private const string Fight = P.Battle + "/rightSideUI/fightBossButton";
    private const string GoBackStage = P.Battle + "/topSideUI/stageProgression/goBackStage";
    private MelonPreferences_Entry<int> _minutes;
    private MelonPreferences_Entry<bool> _fallback;
    private MelonPreferences_Entry<int> _fallbackStages;
    private MelonPreferences_Entry<int> _fallbackMinutes;
    private int _lastAttemptStage;
    private int _trackedStage;
    private DateTime _stageSince;
    private bool _noControlLogged;
    protected override bool EnabledByDefault => true;

    /// <summary>Pressing "Boss" works with a menu open, so the player's screen is never closed for it.</summary>
    internal override bool NeedsCleanScreen => false;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _minutes = c.CreateEntry("every_minutes", 1, "Minutes Between Tries", "How long to farm before trying the boss again.");
        _fallback = c.CreateEntry("stage_fallback", false, "Stage Fallback",
            "After a lost boss, or too long at one stage, step back a few stages to farm, then try again after " +
            "Minutes Between Tries. Default: off.");
        _fallbackStages = c.CreateEntry("fallback_stages", 3, "Stages To Fall Back",
            "How many stages to step back. 1 to 20. Default: 3.");
        _fallbackMinutes = c.CreateEntry("fallback_minutes", 30, "Max Minutes At One Stage",
            "Fall back after this long at the same stage even without a lost boss. 0 = only after a lost boss. " +
            "Up to 240. Default: 30.");
    }

    public override IEnumerator Execute()
    {
        var now = DateTime.Now;
        NextRunTime = now.AddMinutes(Math.Max(1, _minutes.Value));
        var stageKnown = PlayerStats.TryGetCurrentStage(out var stage);
        if (stageKnown && stage != _trackedStage)
        {
            _trackedStage = stage;
            _stageSince = now;
        }

        if (!Ui.Clickable(Fight)) yield break;
        if (!stageKnown)
            Logger.Warning("[Boss Retry] Stage data unavailable; this retry will not count toward prestige.");

        var reason = stageKnown
            ? StageFallbackPolicy.Decide(_fallback.Value, stage, _lastAttemptStage, true, _stageSince, now,
                _fallbackMinutes.Value)
            : StageFallbackReason.None;
        if (reason != StageFallbackReason.None)
        {
            var target = StageFallbackPolicy.Target(stage, _fallbackStages.Value);
            Logger.Info(reason == StageFallbackReason.LostBoss
                ? $"[Boss Retry] Lost the boss at stage {stage}; falling back to stage {target}."
                : $"[Boss Retry] {_fallbackMinutes.Value} minutes at stage {stage}; falling back to stage {target}.");
            var reached = stage;
            yield return FallBack(stage, target, value => reached = value);
            _lastAttemptStage = 0;
            if (reached < stage)
            {
                _trackedStage = reached;
                _stageSince = DateTime.Now;
                Logger.Info($"[Boss Retry] Now farming stage {reached}; next try in {Math.Max(1, _minutes.Value)} minute(s).");
                yield break;
            }
        }

        yield return Ui.Click(Fight, 1f, stageKnown ? $"fight boss at stage {stage}" : "fight boss (stage unavailable)");
        if (stageKnown)
        {
            _lastAttemptStage = stage;
            var attempts = BossAttemptTracker.RecordAttempt(stage);
            Logger.Info($"[Boss Retry] Attempt {attempts} at stage {stage}.");
        }
    }

    /// <summary>
    ///     Presses the battle screen's previous-stage control until the target is reached. Each press must lower
    ///     the live stage, read from the game itself, or the fallback stops: an unrecognised control is never
    ///     pressed twice.
    /// </summary>
    private IEnumerator FallBack(int stage, int target, Action<int> reached)
    {
        var control = FindPreviousStageControl();
        if (control == null)
        {
            if (!_noControlLogged)
            {
                _noControlLogged = true;
                Logger.Warning("[Boss Retry] Stage Fallback could not find a previous-stage control on the battle " +
                               "screen; pressing Boss instead. Press F9 on the battle screen and attach the screen map " +
                               "to a GitHub issue so it can be supported.");
            }

            reached(stage);
            yield break;
        }

        var current = stage;
        for (var press = 0; press < (stage - target) * 2 && current > target; press++)
        {
            // Right after a step the game can leave the arrow switched off for a moment while the stage changes. Wait
            // for it instead of stopping after one stage (issue #10: 2 stages asked, 1 taken).
            var ready = Ui.Clickable(control);
            if (!ready)
                yield return Ui.WaitUntil(() => Ui.Clickable(control), value => ready = value, 3f);
            if (!ready)
            {
                Logger.Warning($"[Boss Retry] '{control.name}' stayed unavailable; Stage Fallback stopped at {current}.");
                break;
            }

            var before = current;
            yield return Ui.Click(control, 1f, $"previous stage ({before} to {before - 1})");
            var changed = false;
            yield return Ui.WaitUntil(() => PlayerStats.TryGetCurrentStage(out var now) && now < before,
                value => changed = value, 2f);

            // The arrow is a hold-to-repeat control (RepeatAction): the game may listen for the pointer going
            // down and up, not for Button.onClick. Try a real press, then a short hold, before giving up.
            if (!changed)
            {
                Ui.PointerClick(control);
                yield return Ui.WaitUntil(() => PlayerStats.TryGetCurrentStage(out var now) && now < before,
                    value => changed = value, 2f);
                if (changed) Logger.Info("[Boss Retry] The previous-stage arrow answers a pointer press.");
            }

            if (!changed)
            {
                yield return Ui.PointerHold(control, 0.6f);
                yield return Ui.WaitUntil(() => PlayerStats.TryGetCurrentStage(out var now) && now < before,
                    value => changed = value, 3f);
                if (changed) Logger.Info("[Boss Retry] The previous-stage arrow answers a pointer hold.");
            }

            if (!changed || !PlayerStats.TryGetCurrentStage(out current) || current >= before)
            {
                Logger.Warning($"[Boss Retry] '{control.name}' did not lower the stage; Stage Fallback stopped at {before}.");
                current = before;
                break;
            }
        }

        reached(current);
    }

    /// <summary>
    ///     The battle screen's own "previous stage" arrow. First by name (it has to say both that it goes back and
    ///     that it is about the stage), then by position: the arrow sits next to the stage number, so the nearest
    ///     button to the left of the label that shows the current stage is the one.
    /// </summary>
    private static Transform FindPreviousStageControl()
    {
        var exact = Ui.Find(GoBackStage);
        if (Ui.Clickable(exact)) return exact;
        var root = Ui.Find(P.Battle);
        if (root == null) return null;
        Transform best = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(false))
        {
            if (t == null || !Ui.Clickable(t)) continue;
            var name = t.name.ToLowerInvariant();
            var parent = t.parent != null ? t.parent.name.ToLowerInvariant() : "";
            var back = name.Contains("previous") || name.Contains("prev") || name.Contains("back") ||
                       name.Contains("left") || name.Contains("down");
            var aboutStage = name.Contains("stage") || name.Contains("level") || name.Contains("wave") ||
                             parent.Contains("stage") || parent.Contains("level") || parent.Contains("wave");
            if (!back || !aboutStage || name.Contains("close")) continue;
            if (best == null || name.Contains("stage")) best = t;
        }

        return best ?? FindArrowBesideStageNumber(root);
    }

    private static Transform FindArrowBesideStageNumber(Transform root)
    {
        if (!PlayerStats.TryGetCurrentStage(out var stage)) return null;
        var label = FindStageLabel(root, stage);
        if (label == null) return null;
        var at = label.position;
        Transform best = null;
        var bestDistance = float.MaxValue;
        foreach (var t in root.GetComponentsInChildren<Transform>(false))
        {
            if (t == null || t == label || !Ui.Clickable(t)) continue;
            var name = t.name.ToLowerInvariant();
            if (name.Contains("fight") || name.Contains("boss") || name.Contains("close") || name.Contains("next") ||
                name.Contains("right") || name.Contains("up")) continue;
            if (!string.IsNullOrEmpty(FirstText(t))) continue; // an arrow carries no words
            var dx = at.x - t.position.x;
            var dy = Math.Abs(at.y - t.position.y);
            if (dx <= 0f || dy > dx) continue; // left of the number and roughly level with it
            var distance = dx + dy * 2f;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = t;
            }
        }

        return best;
    }

    /// <summary>The visible label that shows the current stage number (not a price, timer or other count).</summary>
    private static Transform FindStageLabel(Transform root, int stage)
    {
        Transform best = null;
        var bestLength = int.MaxValue;
        foreach (var t in root.GetComponentsInChildren<Transform>(false))
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            var text = Ui.Text(t);
            if (string.IsNullOrEmpty(text) || text.Length > 16) continue;
            var matches = false;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\d+"))
                if (int.TryParse(m.Value, out var n) && n == stage)
                    matches = true;
            if (!matches || text.Contains(":") || text.Contains("%") || text.Contains("/")) continue;
            if (text.Length < bestLength)
            {
                bestLength = text.Length;
                best = t;
            }
        }

        return best;
    }

    private static string FirstText(Transform button)
    {
        foreach (var t in button.GetComponentsInChildren<Transform>(false))
        {
            var text = Ui.Text(t);
            if (!string.IsNullOrEmpty(text)) return text;
        }

        return string.Empty;
    }
}
