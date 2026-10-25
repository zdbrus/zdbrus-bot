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
///     Shared "upgrade tree" buyer: opens each node's preview, reads name / level / cost, then buys in the
///     priority order the user set (unlisted nodes after, cheapest first) while the currency covers the cost.
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

    private static int Rank(List<string> order, string name)
    {
        var i = order.IndexOf((name ?? string.Empty).Trim().ToLowerInvariant());
        return i < 0 ? order.Count : i;
    }

    public static IEnumerator Run(List<Transform> nodes, Layout l, Func<double> currency, string priority,
        double keep = 0, int maxPerNode = 10)
    {
        var info = new List<(Transform node, string name, double cost, bool open)>();
        foreach (var node in nodes)
        {
            yield return Ui.Click(node, 0.9f);
            if (!Ui.Visible(l.Preview)) continue;
            var name = Ui.Text(l.Preview + "/" + l.Name);
            var level = Ui.Text(l.Preview + "/" + l.Level);
            var m = Regex.Match(level ?? "", @"(\d+)\s*/\s*(\d+)");
            var maxed = (level ?? "").IndexOf("max", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (m.Success && int.Parse(m.Groups[1].Value) >= int.Parse(m.Groups[2].Value));
            var buy = l.Preview + "/" + l.Buy;
            var cost = Ui.NumberAt(l.Preview + "/" + l.Cost);
            info.Add((node, name, cost, !maxed && Ui.Visible(buy) && cost > 0));
            Logger.Debug($"[Nodes] {name} | {level} | cost {cost} | buy {(Ui.Visible(buy) ? (Ui.Clickable(buy) ? "on" : "off") : "hidden")}");
            yield return Ui.Click(l.Preview + "/" + l.Close, 0.5f);
        }

        var order = (priority ?? string.Empty).Split(',').Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0).ToList();

        Logger.Info($"[Nodes] {info.Count(u => u.open)} of {info.Count} upgradable, currency {currency()}");
        foreach (var up in info.Where(u => u.open).OrderBy(u => Rank(order, u.name)).ThenBy(u => u.cost).ToList())
        {
            if (currency() - keep < up.cost) continue;
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
                yield return Ui.Click(buy, 1.2f, up.name);
                if (Ui.MissingCurrency) break;
            }

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
    private MelonPreferences_Entry<string> _priority;
    private MelonPreferences_Entry<int> _keep;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _priority = c.CreateEntry("priority",
            "Attribute damage,All main attributes,Raining gold,Hero level up cost,Attribute health,Attribute armor," +
            "Firestone finder,Powerless boss,Powerless enemy", "Priority",
            "Meteorite research names in buy order, comma-separated. Unlisted ones are bought after, cheapest first.");
        _keep = c.CreateEntry("keep_meteorites", 0, "Meteorites To Keep", "Meteorites to leave unspent.");
    }

    public override IEnumerator Execute()
    {
        if (Cooldown.Recent(this, 20)) yield break;
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
        if (Cooldown.Recent(this, 20)) yield break;
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
        if (Cooldown.Recent(this, 20)) yield break;
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

        if (_vault.Value && Ui.Clickable(H + "/actionButtons/pharaohVault"))
        {
            yield return Ui.Click(H + "/actionButtons/pharaohVault", 1.5f);
            for (var n = 0; n < 10 && Ui.Visible(V); n++)
            {
                var cost = Ui.NumberAt(V + "/openButton/layout/costText");
                if (cost <= 0 || Ui.NumberAt(Coins) < cost || !Ui.Clickable(V + "/openButton")) break;
                yield return Ui.Click(V + "/openButton", 4f, "pharaoh's vault");
                yield return Ui.CloseRewards();
                if (Ui.MissingCurrency) break;
            }

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
    private MelonPreferences_Entry<int> _stallHours;
    private MelonPreferences_Entry<int> _minGain;
    private MelonPreferences_Entry<int> _minHours;
    private MelonPreferences_Entry<int> _bestStage;
    private MelonPreferences_Entry<string> _bestAt;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _stallHours = c.CreateEntry("stall_hours", 3, "Hours Without Progress",
            "Empower once no new highest stage has been reached for this many hours (the run has stalled).");
        _minGain = c.CreateEntry("min_gain_percent", 100, "Minimum Gold Boost %",
            "Never empower for less than this extra gold (the +% the temple shows).");
        _minHours = c.CreateEntry("min_hours_played", 1, "Minimum Hours In Run", "Never empower a run younger than this.");
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
        NextRunTime = DateTime.Now.AddMinutes(30);
        var stalled = HoursSinceProgress();
        if (stalled < _stallHours.Value)
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
        Logger.Info($"Temple: stalled at stage {_bestStage.Value} for {stalled:0.#}h, gold boost +{gain}%, run {played:0.#}h.");

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
        yield return Ui.Click(empower.transform, 3f, "empower (prestige)");

        // The game may ask to confirm; press only an obvious confirm button in a newly opened popup.
        foreach (var confirmPopup in Ui.VisibleChildren(P.Popups).Where(t => !before.Contains(t.GetInstanceID())).ToList())
        {
            var yes = confirmPopup.GetComponentsInChildren<UnityEngine.UI.Button>()
                .FirstOrDefault(b => b.interactable && b.gameObject.activeInHierarchy &&
                                     Regex.IsMatch(b.name, "^(confirm|yes|ok|accept|empower)", RegexOptions.IgnoreCase) &&
                                     !b.name.Contains("2x"));
            if (yes == null) continue;
            yield return Ui.Click(yes.transform, 5f, "confirm empower");
            break;
        }

        _bestStage.Value = 0;
        _bestAt.Value = "";
        MelonPreferences.Save();
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
        if (Cooldown.Recent(this, 20)) yield break;
        NextRunTime = DateTime.Now.AddHours(6);
        yield return Ui.OpenTownBuilding("tavern");
        yield return Ui.Click(P.Popups + "/TavernSelection/bg/scarabGame", 2.5f);
        yield return Ui.Click(S + "/helpCanvas/actionButtons/pharaohVault", 1.5f);
        yield return Ui.Click(V + "/rightSideUI/milestones", 1.5f);
        var list = Ui.Find(List);
        if (list != null)
        {
            for (var i = 0; i < list.childCount; i++)
            {
                var claim = list.GetChild(i).Find("rewardRoot/claimButton");
                if (!Ui.Clickable(claim)) continue;
                yield return Ui.Click(claim, 1.5f, "scarab milestone");
                yield return Ui.CloseRewards();
            }
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
    private static readonly Regex FreeButton =
        new(@"^(claim|collect|free|getfree|claimall|claimbutton|collectbutton|claimallbutton|collectall)", RegexOptions.IgnoreCase);

    private MelonPreferences_Entry<string> _bubbles;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _bubbles = c.CreateEntry("bubbles",
            "SignIn,LockerRewards,PiratesPrize,HallOfHeroes,TowerOfSouls,Prophet,Mercenaries,AncientArtifact," +
            "TalentAvailable,GuardianEvolution,GuardianRarity,WarMachinesRarity,WarmachineUnlocked," +
            "ScarabGameBeastRelease,UnlockLedra,UnlockYamanoth,UnlockKramatak,BattleLive",
            "Bubbles", "Notification bubbles to open for free rewards (comma-separated bubble names).");
    }

    private static HashSet<int> OpenRoots()
    {
        var set = new HashSet<int>();
        foreach (var root in new[] { P.Menus, P.Popups, P.Events })
            foreach (var t in Ui.VisibleChildren(root))
                set.Add(t.GetInstanceID());
        return set;
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
        var names = (_bubbles.Value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        foreach (var name in names)
        {
            var path = P.Notifications + "/" + name;
            if (!Ui.Visible(path)) continue;

            var before = OpenRoots();
            yield return new Firebot.GameModel.Primitives.GameNotificationButton(path).Click();
            yield return new WaitForSeconds(2f);

            var opened = new List<Transform>();
            foreach (var root in new[] { P.Menus, P.Popups, P.Events })
                opened.AddRange(Ui.VisibleChildren(root).Where(t => !before.Contains(t.GetInstanceID())));

            var claimed = 0;
            foreach (var screen in opened)
            {
                for (var pass = 0; pass < 10; pass++)
                {
                    var btn = screen.GetComponentsInChildren<UnityEngine.UI.Button>()
                        .FirstOrDefault(b => b.interactable && b.gameObject.activeInHierarchy &&
                                             FreeButton.IsMatch(b.name) && !HasPrice(b.transform));
                    if (btn == null) break;
                    yield return Ui.Click(btn.transform, 1.5f, $"{name}: {btn.name}");
                    claimed++;
                    yield return Ui.CloseRewards();
                }
            }

            Logger.Info($"[Bubbles] {name}: opened {string.Join(", ", opened.Select(o => o.name))}; claimed {claimed}");

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
        if (Cooldown.Recent(this, 20)) yield break;
        NextRunTime = DateTime.Now.AddHours(6);
        yield return Ui.OpenTownBuilding("tavern");
        yield return Ui.Click(P.Popups + "/TavernSelection/bg/scarabGame", 2.5f);
        yield return Ui.Click(S + "/helpCanvas/actionButtons/pharaohVault", 1.5f);
        for (var n = 0; n < 10 && Ui.Visible(V); n++)
        {
            var cost = Ui.NumberAt(V + "/openButton/layout/costText");
            if (cost <= 0 || Ui.NumberAt(Coins) < cost || !Ui.Clickable(V + "/openButton")) break;
            yield return Ui.Click(V + "/openButton", 4f, "pharaoh's vault");
            yield return Ui.CloseRewards();
            if (Ui.MissingCurrency) break;
        }

        if (Ui.Visible(V)) yield return Ui.Click(V + "/closeButton", 0.8f);
        if (Ui.Visible(S)) yield return Ui.Click(S + "/closeButton");
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
                yield return Ui.Click(buy, 1.2f, "event exchange: " + want);
                yield return Ui.CloseRewards();
                if (Ui.MissingCurrency) yield break;
            }
        }

        var close = shop.Find("bg/closeButton");
        if (close != null) yield return Ui.Click(close, 0.8f);
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
        for (var step = 0; step <= 20 && content != null; step++)
        {
            if (rect != null) rect.horizontalNormalizedPosition = step / 20f;
            yield return new WaitForSeconds(0.4f);
            for (var i = 0; i < content.childCount; i++)
            {
                var claim = content.GetChild(i).Find("freeBg/glowOutlineFree/rewardRoot/claimButton");
                if (!Ui.Clickable(claim)) continue;
                yield return Ui.Click(claim, 1.2f, "battle pass free reward");
                yield return Ui.CloseRewards();
                claimed++;
            }
        }

        Logger.Debug($"[BattlePass] claimed {claimed}");
        yield return Ui.Click(B + "/closeButton");
    }
}
