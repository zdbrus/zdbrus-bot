using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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

    private static string KeyOf(Transform node)
    {
        var parts = new List<string>();
        for (var t = node; t != null && parts.Count < 6; t = t.parent)
            parts.Add(t.name + "#" + t.GetSiblingIndex());
        return string.Join("/", parts);
    }

    /// <summary>
    ///     True when a node's earlier preview already shows it cannot be bought now: maxed, a known cost above
    ///     the budget, or locked recently. Skips opening its preview again.
    /// </summary>
    internal static bool CanSkip(Transform node, double budget, bool retryUnaffordableAfterCooldown = true)
    {
        if (!Known.TryGetValue(KeyOf(node), out var k)) return false;
        if (k.Maxed) return true;
        var age = DateTime.Now - k.At;
        if (k.Cost > 0)
            return budget < k.Cost && (!retryUnaffordableAfterCooldown || age < TimeSpan.FromHours(6));
        return age < TimeSpan.FromHours(2);
    }

    internal static void Remember(Transform node, bool maxed, double cost, string name = null)
    {
        var key = KeyOf(node);
        if (name == null && Known.TryGetValue(key, out var previous)) name = previous.Name;
        Known[key] = new NodeInfo { Maxed = maxed, Cost = cost, Name = name, At = DateTime.Now };
    }

    internal static bool TryGetKnown(Transform node, out bool maxed, out double cost, out string name)
    {
        if (Known.TryGetValue(KeyOf(node), out var info))
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

    internal static void Forget(Transform node) => Known.Remove(KeyOf(node));

    private static int Rank(List<string> order, string name)
    {
        var i = order.IndexOf((name ?? string.Empty).Trim().ToLowerInvariant());
        return i < 0 ? order.Count : i;
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
            () => Ui.Text(levelPath) != levelBefore ||
                  Ui.NumberAt(costPath) != costBefore ||
                  currency() < currencyBefore,
            result, name, 1.2f);
    }

    public static IEnumerator Run(List<Transform> nodes, Layout l, Func<double> currency, string priority,
        double keep = 0, int maxPerNode = 10, bool stopAtFirstAffordable = false)
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
            if (CanSkip(node, currency() - keep))
            {
                if (TryGetKnown(node, out var knownMaxed, out var knownCost, out var knownName) &&
                    !knownMaxed && knownCost > 0 && !string.IsNullOrEmpty(knownName))
                    info.Add((node, knownName, knownCost, true));
                skipped++;
                continue;
            }

            yield return Ui.Click(node, 0.9f);
            if (!Ui.Visible(l.Preview)) continue;
            var name = Ui.Text(l.Preview + "/" + l.Name);
            var level = Ui.Text(l.Preview + "/" + l.Level);
            var m = Regex.Match(level ?? "", @"(\d+)\s*/\s*(\d+)");
            var maxed = (level ?? "").IndexOf("max", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (m.Success && int.Parse(m.Groups[1].Value) >= int.Parse(m.Groups[2].Value));
            var buy = l.Preview + "/" + l.Buy;
            var cost = Ui.NumberAt(l.Preview + "/" + l.Cost);
            var open = !maxed && Ui.Visible(buy) && cost > 0;
            Remember(node, maxed, open ? cost : 0, name);
            if (stopAtFirstAffordable && open && currency() - keep >= cost)
            {
                Logger.Info($"[Nodes] First affordable: {name} ({cost}), have {currency()}.");
                Forget(node);
                for (var n = 0; n < maxPerNode; n++)
                {
                    cost = Ui.NumberAt(l.Preview + "/" + l.Cost);
                    if (cost <= 0 || currency() - keep < cost || !Ui.Clickable(buy))
                    {
                        Logger.Debug($"[Nodes] stop {name}: cost {cost}, have {currency()}, clickable {Ui.Clickable(buy)}");
                        break;
                    }

                    var changed = false;
                    yield return BuyOne(l, name, currency, keep, value => changed = value);
                    if (Ui.MissingCurrency) break;
                    if (!changed)
                    {
                        Logger.Warning($"[Nodes] Stopping after {name} made no verified progress.");
                        break;
                    }
                }

                if (Ui.Visible(l.Preview)) yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
                yield break;
            }

            info.Add((node, name, cost, open));
            Logger.Debug($"[Nodes] {name} | {level} | cost {cost} | buy {(Ui.Visible(buy) ? (Ui.Clickable(buy) ? "on" : "off") : "hidden")}");
            yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
        }

        var order = (priority ?? string.Empty).Split(',').Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0).ToList();

        Logger.Info($"[Nodes] {info.Count(u => u.open)} of {info.Count} previewed upgradable " +
                    $"({skipped} skipped from earlier reads), currency {currency()}");
        foreach (var up in info.Where(u => u.open).OrderBy(u => Rank(order, u.name)).ThenBy(u => u.cost).ToList())
        {
            if (currency() - keep < up.cost)
            {
                Logger.Info($"[Nodes] Stopping at {up.name}: cost {up.cost}, have {currency() - keep}. Lower tiers stay untouched.");
                break;
            }

            yield return Ui.Click(up.node, 0.9f);
            var buy = l.Preview + "/" + l.Buy;
            for (var n = 0; n < maxPerNode; n++)
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

            var finalLevel = Ui.Text(l.Preview + "/" + l.Level);
            var finalMatch = Regex.Match(finalLevel ?? "", @"(\d+)\s*/\s*(\d+)");
            var finalMaxed = (finalLevel ?? "").IndexOf("max", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (finalMatch.Success && int.Parse(finalMatch.Groups[1].Value) >= int.Parse(finalMatch.Groups[2].Value));
            var finalCost = Ui.NumberAt(l.Preview + "/" + l.Cost);
            Remember(up.node, finalMaxed || !Ui.Visible(buy), finalMaxed || !Ui.Visible(buy) ? 0 : finalCost, up.name);
            if (Ui.Visible(l.Preview)) yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
            if (Ui.MissingCurrency) yield break;
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

public class MeteoriteResearchTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/MeteoriteResearch";

    private const string L = P.Menus + "/Library";
    private const string M = L + "/submenus/meteoriteResearch";
    private const string Meteorites = L + "/counters/currencyInteraction (Meteorite)/quantity";
    private MelonPreferences_Entry<int> _keep;
    private MelonPreferences_Entry<string> _priority;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _keep = c.CreateEntry("keep_meteorites", 0, "Meteorites To Keep", "Meteorites to leave unspent.");
        _priority = c.CreateEntry("priority",
            "Attribute damage,All main attributes,Raining gold,Hero level up cost,Attribute health,Attribute armor,Firestone finder,Powerless boss,Powerless enemy",
            "Priority", "Meteorite research upgrades are filled in this order; lower tiers wait until higher tiers are maxed.");
    }

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(20);
        NextRunTime = DateTime.Now.AddHours(4);
        yield return Ui.OpenTownBuilding("library");
        yield return Ui.WaitFor(L);
        yield return Ui.Click(L + "/submenuButtons/meteoriteResearch", 1.5f);
        if (!Ui.Visible(M)) yield break;

        var tree = Ui.VisibleChildren(M + "/submenus").FirstOrDefault();
        if (tree == null) yield break;
        var nodes = new List<Transform>();
        for (var i = 0; i < tree.childCount; i++)
        {
            var c = tree.GetChild(i);
            if (c.name.StartsWith("research") && !c.name.StartsWith("researchPath") && c.gameObject.activeInHierarchy)
                nodes.Add(c);
        }

        yield return NodeBuyer.Run(nodes, new NodeBuyer.Layout
        {
            Preview = P.Popups + "/MeteoriteResearchPreview/bg",
            Name = "researchName", Level = "level", Buy = "unlocked/researchButton",
            Cost = "unlocked/researchButton/cost", Close = "closeButton"
        }, () => Ui.NumberAt(Meteorites), _priority.Value, _keep.Value);

        yield return Ui.Click(L + "/closeButton");
    }
}

public class ForbiddenKnowledgeTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/ForbiddenKnowledge";

    private const string F = P.Menus + "/ForbiddenKnowledge";
    private MelonPreferences_Entry<string> _priority;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority", "Firestone Finder,All main attributes,Attribute damage,Raining gold",
            "Priority", "Forbidden knowledge upgrades in buy order. Unlisted ones are bought after, cheapest first.");
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
            }, Tomes, _priority.Value);
        }

        yield return Ui.Click(F + "/closeButton");
    }
}

public class ScarabGameTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/ScarabGame";

    private const string S = P.Menus + "/ScarabGame";
    private const string H = S + "/helpCanvas";
    private const string Nobles = H + "/counters/currencyInteraction (NoblesToken)/quantity";
    private const string Play = H + "/playButton";
    private const string V = P.Menus + "/PharaohsVault";
    private const string Coins = V + "/counters/currencyInteraction (AncientCoin)/quantity";
    private MelonPreferences_Entry<bool> _play;
    private MelonPreferences_Entry<bool> _vault;

    public override int MinimumLevel => LevelRequirements.ScarabGameShop;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _play = c.CreateEntry("play", true, "Play Scarab's Game", "Spins with Noble's tokens (never Pharaoh's tokens or gems).");
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
            // Keep it on manual and single spins so every spin is counted.
            if (!Ui.Text(H + "/bottomRightUI/autoPlayToggle/text").Equals("Manual", StringComparison.OrdinalIgnoreCase))
                yield return Ui.Click(H + "/bottomRightUI/autoPlayToggle", 0.8f);

            for (var n = 0; n < 60; n++)
            {
                var have = Ui.NumberAt(Nobles);
                var cost = Ui.NumberAt(Play + "/layout/costText1");
                if (have <= 0 || cost <= 0 || have < cost || !Ui.Clickable(Play)) break;
                yield return Ui.Click(Play, 3.5f, "scarab spin");
                yield return Ui.CloseRewards();
                if (Ui.MissingCurrency) break;
                // Stop if the spin did not use a Noble's token (the button changed currency).
                for (var w = 0; w < 12 && Ui.NumberAt(Nobles) >= have; w++) yield return new WaitForSeconds(0.5f);
                if (Ui.NumberAt(Nobles) >= have) { Logger.Info("[Scarab] Spin did not use a Noble's token; stopping."); break; }
                // Wait for the reels to stop before the next spin.
                for (var w = 0; w < 16 && !Ui.Clickable(Play); w++) yield return new WaitForSeconds(0.5f);
            }
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

/// <summary>
///     Prestige ("Empower now") once the run has stalled: no new highest stage for a few hours, and the temple offers at
///     least a minimum gold boost. Never uses the paid 2x empower.
/// </summary>
public class TemplePrestigeTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/TemplePrestige";

    private const string T = P.Menus + "/TempleOfEternals";
    private const string Info = T + "/submenus/bgNew/prestigeSubmenu/adventureInfo";
    private const string Stage = P.Battle + "/topSideUI/stageProgression/currentStage/stageNum";
    private const string PrestigeComplete = P.Popups + "/TOEPrestigeComplete";
    private MelonPreferences_Entry<int> _stallHours;
    private MelonPreferences_Entry<int> _minGain;
    private MelonPreferences_Entry<int> _minHours;
    private MelonPreferences_Entry<int> _bestStage;
    private MelonPreferences_Entry<string> _bestAt;
    private MelonPreferences_Entry<int> _efficiency;
    private MelonPreferences_Entry<double> _lastGain;
    private MelonPreferences_Entry<string> _lastGainAt;
    private MelonPreferences_Entry<double> _refRate;
    private int _slowStreak;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _stallHours = c.CreateEntry("stall_hours", 3, "Hours Without Progress",
            "Empower once no new highest stage has been reached for this many hours (the run has stalled).");
        _minGain = c.CreateEntry("min_gain_percent", 100, "Minimum Gold Boost %",
            "Never empower for less than this extra gold (the +% the temple shows).");
        _minHours = c.CreateEntry("min_hours_played", 1, "Minimum Hours In Run", "Never empower a run younger than this.");
        _efficiency =         c.CreateEntry("efficiency_percent", 75, "Empower When Gain Rate Below %",
                    "Empower once the gold boost's current growth per hour stays below this % of this run's own average growth per hour (the point where waiting lowers your long-run average). Lower values wait longer to cover the time a new run needs to ramp up. 0 turns this rule off; the stall rule above still applies.");
        _lastGain = c.CreateEntry("last_gain", -1.0, "Last Gold Boost Sample", "Kept by the bot.", is_hidden: true);
        _lastGainAt = c.CreateEntry("last_gain_at", "", "Last Sample Time", "Kept by the bot.", is_hidden: true);
        _refRate = c.CreateEntry("reference_rate", 0.0, "Previous Run Gain Rate", "Kept by the bot.", is_hidden: true);
        _bestStage = c.CreateEntry("best_stage", 0, "Best Stage", "Highest stage seen this run (kept by the bot).", is_hidden: true);
        _bestAt = c.CreateEntry("best_stage_at", "", "Best Stage Time", "When the highest stage was reached (kept by the bot).", is_hidden: true);
    }

    private static double GainPercent(string text)
    {
        var m = Regex.Match((text ?? "").Replace(",", ""), @"\+\s*([\d.]+)\s*([A-Za-z]*)\s*%");
        if (!m.Success) return -1;
        var v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var suffix = m.Groups[2].Value.ToUpperInvariant();
        return suffix switch { "" => v, "K" => v * 1e3, "M" => v * 1e6, "B" => v * 1e9, _ => double.MaxValue };
    }

    /// <summary>Tracks the highest stage and when it was reached. Returns hours since the last new best.</summary>
    private double HoursSinceProgress()
    {
        var stage = (int)Ui.NumberAt(Stage);
        if (stage <= 0) return 0;
        var now = DateTime.Now;
        DateTime.TryParse(_bestAt.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at);
        // A much lower stage means a new run started (prestige done by hand or by the bot).
        if (stage > _bestStage.Value || stage < _bestStage.Value / 2 || at == default)
        {
            _bestStage.Value = stage;
            _bestAt.Value = now.ToString("o");
            MelonPreferences.Save();
            return 0;
        }

        return (now - at).TotalHours;
    }

    public override IEnumerator Execute()
    {
        // Its bubble stays lit the whole run, so check at most every 15 minutes.
        BubbleQuietUntil = DateTime.Now.AddMinutes(15);
        NextRunTime = DateTime.Now.AddMinutes(30);
        var stalled = HoursSinceProgress();
        var isStalled = stalled >= _stallHours.Value;
        var sampleDue = _efficiency.Value > 0 &&
                        (!DateTime.TryParse(_lastGainAt.Value, CultureInfo.InvariantCulture,
                             DateTimeStyles.RoundtripKind, out var sampledAt) ||
                         (DateTime.Now - sampledAt).TotalMinutes >= 60);
        if (!isStalled && !sampleDue)
        {
            Logger.Debug($"Temple: best stage {_bestStage.Value}, {stalled:0.#}h without a new one (empowers after {_stallHours.Value}h).");
            yield break;
        }

        yield return Ui.OpenTownBuilding("templeOfEternals");
        yield return Ui.WaitFor(T);
        if (!Ui.Visible(T)) yield break;

        var gain = GainPercent(Ui.Text(Info + "/empowerInfo"));
        var playedText = Ui.Text(Info + "/adventureTimePlayed") ?? "";
        var played = TimeSpan.TryParse(playedText, out var ts) ? ts.TotalHours : 0;
        var dayMatch = Regex.Match(playedText, @"(\d+)d");
        if (dayMatch.Success) played = Math.Max(played, int.Parse(dayMatch.Groups[1].Value) * 24);
        Logger.Info($"Temple: stage {_bestStage.Value}, {stalled:0.#}h without a new best, gold boost +{gain}%, run {played:0.#}h.");

        if (!isStalled)
        {
            var reason = EvaluateRate(gain, played, out var inefficient);
            if (!inefficient)
            {
                Logger.Info($"Temple: keeping the run ({reason}).");
                yield return Ui.Click(T + "/closeButton");
                yield break;
            }

            Logger.Info($"Temple: gain rate dropped ({reason}); empowering if the minimums are met.");
        }

        if (gain < _minGain.Value || played < _minHours.Value || !Ui.Clickable(Info + "/openEmpowerButton"))
        {
            yield return Ui.Click(T + "/closeButton");
            yield break;
        }

        // "Empower now" opens the empower popup; press its normal empower button (never the paid 2x one).
        var before = new HashSet<int>(Ui.VisibleChildren(P.Popups).Select(t => t.GetInstanceID()));
        yield return Ui.Click(Info + "/openEmpowerButton", 2f, "Empower now");
        var popup = Ui.VisibleChildren(P.Popups).FirstOrDefault(t => !before.Contains(t.GetInstanceID()));
        if (popup == null)
        {
            Logger.Warning("Temple: the empower popup did not open; skipped.");
            yield break;
        }

        var empower = popup.GetComponentsInChildren<UnityEngine.UI.Button>()
            .FirstOrDefault(b => b.name == "empowerButton" && b.interactable && b.gameObject.activeInHierarchy);
        if (empower == null)
        {
            Logger.Warning($"Temple: no empower button in {popup.name}; skipped.");
            yield break;
        }

        before = new HashSet<int>(Ui.VisibleChildren(P.Popups).Select(t => t.GetInstanceID()));
        var stageBefore = Ui.NumberAt(Stage);
        yield return Ui.Click(empower.transform, 3f, "empower (prestige)");

        // The game may ask to confirm; press only an obvious confirm button in a newly opened popup.
        var prestigeConfirmed = false;
        foreach (var confirmPopup in Ui.VisibleChildren(P.Popups).Where(t => !before.Contains(t.GetInstanceID())).ToList())
        {
            var yes = confirmPopup.GetComponentsInChildren<UnityEngine.UI.Button>()
                .FirstOrDefault(b => b.interactable && b.gameObject.activeInHierarchy &&
                                     Regex.IsMatch(b.name, "^(confirm|yes|ok|accept|empower)", RegexOptions.IgnoreCase) &&
                                     !b.name.Contains("2x"));
            if (yes == null) continue;
            if (string.Equals(confirmPopup.name, "TOEPrestigeComplete", StringComparison.OrdinalIgnoreCase))
                prestigeConfirmed = true;
            yield return Ui.Click(yes.transform, 5f, "confirm empower");
            break;
        }

        if (!prestigeConfirmed)
        {
            var completionAppeared = false;
            yield return Ui.WaitUntil(() => Ui.Visible(PrestigeComplete),
                value => completionAppeared = value, 5f);
            if (completionAppeared)
            {
                prestigeConfirmed = true;
                yield return Ui.Click(PrestigeComplete + "/bg/confirmButton", 2f, "start after temple prestige");
            }
        }

        var stageResetObserved = false;
        if (stageBefore > 0)
            yield return Ui.WaitUntil(() =>
            {
                var stageAfter = Ui.NumberAt(Stage);
                return stageAfter > 0 && stageAfter < stageBefore;
            }, value => stageResetObserved = value, 5f);

        if (!prestigeConfirmed && !stageResetObserved)
        {
            Logger.Warning("Temple: no prestige completion or stage reset was observed; keeping the stall tracker.");
            yield break;
        }

        if (played > 0 && gain > 0 && gain < 1e300) _refRate.Value = gain / played;
        _slowStreak = 0;
        _lastGain.Value = -1;
        _lastGainAt.Value = "";
        _bestStage.Value = 0;
        _bestAt.Value = "";
        MelonPreferences.Save();
    }

    /// <summary>Compares the gold boost growth per hour since the last sample with the previous run's average.</summary>
    private string EvaluateRate(double gain, double played, out bool inefficient)
    {
        inefficient = false;
        if (gain <= 0 || gain >= 1e300) return "gold boost unreadable";

        var now = DateTime.Now;
        var previous = _lastGain.Value;
        var hasSample = previous >= 0 &&
                        DateTime.TryParse(_lastGainAt.Value, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var sampleAt);
        DateTime.TryParse(_lastGainAt.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out sampleAt);
        var hours = (now - sampleAt).TotalHours;

        if (hasSample && gain >= previous && hours >= 0.25)
        {
            var rate = (gain - previous) / hours;
            var average = played > 0 ? gain / played : 0;
            var limit = average * _efficiency.Value / 100.0;
            _slowStreak = rate < limit ? _slowStreak + 1 : 0;
            inefficient = _slowStreak >= 2;
            _lastGain.Value = gain;
            _lastGainAt.Value = now.ToString("o");
            MelonPreferences.Save();
            return $"+{gain - previous:0.##}% over {hours:0.#}h = {rate:0.##}%/h vs limit {limit:0.##}%/h";
        }

        _lastGain.Value = gain;
        _lastGainAt.Value = now.ToString("o");
        MelonPreferences.Save();
        return "first sample for this run";
    }
}

public class ScarabMilestonesTask : BotTask
{
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
    private MelonPreferences_Entry<string> _priority;
    private MelonPreferences_Entry<int> _keep;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority",
            "Pickaxe,Legendary chest,Meteorite,Golden key,Strange dust,Twilight hourglass,Soul ember,Exotic coin",
            "Items To Buy", "Event exchange items to buy, in order (comma-separated). Items not listed are never bought.");
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
            for (var n = 0; n < 40; n++)
            {
                var cost = Ui.NumberOf(buy?.Find("purchaseCost"));
                var m = Regex.Match(Ui.Text(item.Find("remainingQuantity")) ?? "", @"(\d+)\s*/\s*(\d+)");
                if (m.Success && int.Parse(m.Groups[1].Value) >= int.Parse(m.Groups[2].Value)) break;
                if (cost <= 0 || Have() - _keep.Value < cost || !Ui.Clickable(buy)) break;
                var before = Have();
                var confirmed = false;
                yield return Ui.ClickUntil(buy, () => Have() < before,
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
            if (!itemMaxed && nextCost > 0 && Have() - _keep.Value < nextCost)
            {
                Logger.Info($"[EventExchange] Stopping at {want}: cost {nextCost}, have {Have() - _keep.Value}. Lower tiers stay untouched.");
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
    private const string B = P.Popups + "/BattlePass/bg";
    private const string Scroll = B + "/submenus/rewards/bg/scrollView";

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        yield return Ui.Click(P.Battle + "/bottomSideUIDesktop/pathOfGloryButton", 2f);
        if (!Ui.Visible(B)) yield break;
        yield return Ui.Click(B + "/submenuButtons/rewards", 1.5f);

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
    private const string Bag = P.Battle + "/bottomSideUIDesktop/menuButtons/inventoryButtonUI";
    private const string Inv = P.Menus + "/Inventory";
    private const string InventoryTitle = Inv + "/titleText";
    private const string Content = Inv + "/submenus/items/ScrollView/Viewport/Content";
    private const string Preview = P.Popups + "/ChestOpenPreview/bg";
    private const string Opening = P.Menus + "/ChestOpening";
    private const string NewItems = P.Popups + "/NewItem";
    private static readonly HashSet<string> SupportedChests = new(StringComparer.OrdinalIgnoreCase)
        { "Common", "Uncommon", "Rare", "Epic", "Legendary", "Wooden", "Iron", "Golden", "Comet", "Lunar" };
    private readonly Dictionary<string, MelonPreferences_Entry<bool>> _open = new(StringComparer.OrdinalIgnoreCase);
    protected override bool EnabledByDefault => true;

    // Keyed by the chest tile's object name in the game (its ID), never by the displayed text.
    protected override void OnConfigure(MelonPreferences_Category c)
    {
        foreach (var id in new[] { "Common", "Uncommon", "Rare", "Epic", "Legendary", "Wooden", "Iron", "Golden", "Comet", "Lunar" })
            _open[id] = c.CreateEntry("open_" + id.ToLowerInvariant(), true, "Open " + id + " Chests",
                "Tick to let the bot open this chest type (game id: " + id + "). Untick to keep them.");
    }

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

            Transform open = null;
            for (var attempt = 0; attempt < 20 && open == null; attempt++)
            {
                foreach (var option in new[] { "openxMax", "openx10", "openx1" })
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
        var configuredSkips = owned.Where(t => skip.Contains(t.name.ToLowerInvariant()) && !kept.ContainsKey(t.name))
            .Select(t => $"{t.name} x{Ui.NumberOf(t.Find("quantity"))}").ToList();
        if (configuredSkips.Count > 0)
            Logger.Info($"[Chests] Left unopened by configured keep list: {string.Join(", ", configuredSkips)}.");
        var unsupported = owned.Where(t => !SupportedChests.Contains(t.name))
            .Select(t => $"{t.name} x{Ui.NumberOf(t.Find("quantity"))}").ToList();
        if (unsupported.Count > 0)
            Logger.Info($"[Chests] Left unopened because the chest type is unsupported: {string.Join(", ", unsupported)}.");

        if (Ui.Visible(Inv)) yield return Ui.Click(Inv + "/closeButton");
        Logger.Info($"[Chests] Confirmed {opened} chest opening(s).");
    }
}

/// <summary>
///     Spends free talent points (earned by character level) in the Character > Talents tree. Talents you list are
///     filled first, then the rest in tree order. It stops as soon as no point is left. Only talent points are spent.
/// </summary>
public class TalentsTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/TalentAvailable";
    protected override bool EnabledByDefault => true;

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

    // Talent points shown as "free/total". -1 when unreadable, so the task still tries.
    private static double Free()
    {
        var t = Ui.Text(Points) ?? "";
        return t.Contains('/') ? Ui.Parse(t.Split('/')[0]) : -1;
    }

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(30);
        NextRunTime = DateTime.Now.AddHours(6);
        if (!Ui.Visible(C)) yield return Ui.Click(P.Battle + "/topLeftSideUI/playerAvatar", 1.5f);
        if (!Ui.Visible(C)) yield break;
        yield return Ui.Click(C + "/submenuButtons/talents", 1.5f);

        var spent = 0;
        if (Ui.Visible(Tree) && Free() != 0)
        {
            var order = (_priority.Value ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();
            var nodes = Ui.VisibleChildren(Tree).Where(t => t.name.StartsWith("talentInteraction") && Ui.Clickable(t)).ToList();
            var discovered = new List<(Transform node, string name, bool available)>();
            foreach (var n in nodes)
            {
                yield return Ui.Click(n, 0.6f);
                if (!Ui.Visible(Preview)) continue;
                var name = (Ui.Text(Preview + "/talentName") ?? "").Trim();
                var available = Ui.Clickable(Preview + "/talentUpgrades/upgradeTalentButton");
                discovered.Add((n, name, available));
                yield return Ui.Click(Preview + "/closeButton", 0.3f);
            }

            var selected = new HashSet<Transform>();
            foreach (var wanted in order)
            {
                if (Free() == 0) break;
                var match = discovered.FirstOrDefault(x =>
                    string.Equals(x.name, wanted, StringComparison.OrdinalIgnoreCase));
                if (match.node == null || !match.available) continue;
                selected.Add(match.node);
                yield return Ui.Click(match.node, 0.6f);
                if (!Ui.Visible(Preview)) continue;
                yield return Upgrade(match.name, () => spent++);
                if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.3f);
            }

            foreach (var match in discovered.Where(x => !selected.Contains(x.node) && x.available))
            {
                if (Free() == 0) break;
                yield return Ui.Click(match.node, 0.6f);
                if (!Ui.Visible(Preview)) continue;
                yield return Upgrade(match.name, () => spent++);
                if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.3f);
            }
        }

        Logger.Info($"[Talents] spent {spent} point(s), {Ui.Text(Points)} left");
        if (Ui.Visible(C)) yield return Ui.Click(C + "/closeButton", 1f);
        // Talent changes only stick once saved: the game asks when the window closes.
        const string unsaved = P.Popups + "/UnsavedChanges/bg";
        if (Ui.Visible(unsaved))
        {
            yield return Ui.Click(unsaved + "/saveChangesButton", 1f, "save talents");
            if (Ui.Visible(C)) yield return Ui.Click(C + "/closeButton", 1f);
        }
    }

    private static IEnumerator Upgrade(string name, Action add)
    {
        for (var i = 0; i < 60 && Free() != 0; i++)
        {
            var up = Ui.Find(Preview + "/talentUpgrades/upgradeTalentButton");
            if (!Ui.Clickable(up)) yield break;
            var before = Ui.Text(Preview + "/talentLevelText");
            yield return Ui.Click(up, 0.4f, $"talent {name}");
            if (Ui.MissingCurrency || Ui.Text(Preview + "/talentLevelText") == before) yield break;
            add();
        }
    }
}

/// <summary>
///     After a lost boss fight the game keeps farming the stage until someone presses "Boss". This presses it again
///     every few minutes, once the heroes have had time to level. Losing costs nothing.
/// </summary>
public class BossRetryTask : BotTask
{
    private const string Fight = P.Battle + "/rightSideUI/fightBossButton";
    private MelonPreferences_Entry<int> _minutes;
    protected override bool EnabledByDefault => true;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _minutes = c.CreateEntry("every_minutes", 4, "Minutes Between Tries", "How long to farm before trying the boss again.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddMinutes(Math.Max(1, _minutes.Value));
        if (!Ui.Clickable(Fight)) yield break;
        yield return Ui.Click(Fight, 1f, $"fight boss at stage {Ui.Text(P.Battle + "/topSideUI/stageProgression/currentStage/stageNum")}");
    }
}
