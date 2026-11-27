using System.Collections;
using Firebot.Behaviors.Extra;
using Firebot.Behaviors.Guild;
using Firebot.Core;
using Firebot.GameModel.Primitives;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Regressions for GitHub issues #6 (Stage Fallback) and #8 (routines that misbehaved on 0.7.65).</summary>
internal static class IssueEightTests
{
    private const string PickaxeRoot = "menus/GuildShop/bg/submenus/supplies/items/freePickaxe";
    private const string PickaxeQuantity = PickaxeRoot + "/claimBg/itemBg/itemQuantity";
    private const string PickaxeClaim = PickaxeRoot + "/claimBg/purchaseButton";
    private const string PickaxeTimer = PickaxeRoot + "/nextFreeObj/progressBarBg/timeLeftText";

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Run(IEnumerator routine)
    {
        var steps = 0;
        while (routine.MoveNext())
        {
            if (++steps > 4000) throw new InvalidOperationException("Coroutine did not finish.");
            if (routine.Current is IEnumerator child) Run(child);
            Time.unscaledTime += 0.1f;
        }
    }

    public static void StageFallbackDecisions()
    {
        var now = new DateTime(2026, 11, 25, 12, 0, 0);
        Check(StageFallbackPolicy.Decide(false, 200, 200, true, now.AddHours(-2), now, 30) == StageFallbackReason.None,
            "Stage Fallback acted while turned off.");
        Check(StageFallbackPolicy.Decide(true, 200, 200, true, now, now, 30) == StageFallbackReason.LostBoss,
            "A boss lost at the same stage did not trigger the fallback.");
        Check(StageFallbackPolicy.Decide(true, 200, 0, true, now, now, 30) == StageFallbackReason.None,
            "A first try at a stage fell back without a lost boss.");
        Check(StageFallbackPolicy.Decide(true, 200, 0, true, now.AddMinutes(-31), now, 30) == StageFallbackReason.TimeAtStage,
            "Too long at one stage did not trigger the fallback.");
        Check(StageFallbackPolicy.Decide(true, 200, 0, true, now.AddMinutes(-31), now, 0) == StageFallbackReason.None,
            "A time limit of 0 still fell back on time.");
        Check(StageFallbackPolicy.Decide(true, 200, 200, false, now, now, 30) == StageFallbackReason.None,
            "The fallback acted while a boss fight was still running.");
        Check(StageFallbackPolicy.Decide(true, 1, 1, true, now, now, 30) == StageFallbackReason.None,
            "The fallback tried to go below stage 1.");
        Check(StageFallbackPolicy.Target(200, 3) == 197 && StageFallbackPolicy.Target(2, 5) == 1 &&
              StageFallbackPolicy.Target(200, 99) == 180 && StageFallbackPolicy.Target(200, 0) == 199,
            "The fallback target ignored its limits.");
    }

    public static void PickaxesWaitForThreshold()
    {
        var now = new DateTime(2026, 11, 25, 12, 0, 0);
        var interval = TimeSpan.Zero;
        var at = PickaxePlan.NextCheck(now, now.AddMinutes(40), 26, 30, ref interval);
        Check(interval == TimeSpan.FromMinutes(40) && at == now.AddMinutes(40 + 3 * 40),
            "The pickaxe check did not wait for enough pickaxes to reach the threshold.");
        at = PickaxePlan.NextCheck(now, now.AddMinutes(10), 26, 30, ref interval);
        Check(interval == TimeSpan.FromMinutes(40) && at == now.AddMinutes(10 + 3 * 40),
            "A shorter timer replaced the learned pickaxe interval.");
        at = PickaxePlan.NextCheck(now, now.AddMinutes(40), 0, 30, ref interval);
        Check(at == now.AddHours(12), "The pickaxe wait was not capped.");
        at = PickaxePlan.NextCheck(now, DateTime.MinValue, 3, 30, ref interval);
        Check(at == now.AddMinutes(10), "An unreadable timer was not retried shortly.");

        GameObject.Clear();
        GameText.Texts.Clear();
        GameText.Times.Clear();
        Logger.Messages.Clear();
        var task = new FreePickaxesTask();
        task.InitializeConfig("test");
        task.SetEnabledFromStatus(true);
        ((MelonPreferences_Entry<int>)MelonPreferences.Categories["free_pickaxes"]
            .Entries["pickaxe_claim_threshold"]).Value = 30;
        GameObject.CreatePath("notifications/FreePickaxes");
        var clicks = 0;
        GameObject.CreatePath(PickaxeClaim).AddComponent<Button>().onClick.Action = () => clicks++;
        GameObject.CreatePath("menus/GuildShop/closeButton").AddComponent<Button>();
        GameText.Texts[PickaxeQuantity] = "x5";
        var started = DateTime.Now;
        GameText.Times[PickaxeTimer] = started.AddMinutes(30);
        Check(task.IsNotificationVisible(), "The pickaxe bubble fixture is not visible.");
        Run(task.Execute());
        Check(clicks == 0 && task.NextRunTime >= started.AddHours(2) && task.BubbleQuietUntil >= task.NextRunTime &&
              !task.IsNotificationVisible(),
            "Below the threshold the pickaxe bubble still reopened the shop for the next pickaxe.");
    }

    public static void GameNumbersFollowTheSystemFormat()
    {
        var en = new System.Globalization.CultureInfo("en-US");
        var de = new System.Globalization.CultureInfo("de-DE");
        void Is(string text, System.Globalization.CultureInfo culture, double expected)
        {
            var got = Firebot.Core.GameNumber.Parse(text, culture);
            Check(Math.Abs(got - expected) < 1e-6, $"'{text}' ({culture.Name}) read as {got}, expected {expected}.");
        }

        Is("1.528", de, 1528);       // issue #10: beer shown with a dot for thousands
        Is("1.528", en, 1528);       // same text under another culture: still thousands, three digits follow
        Is("1,528", en, 1528);
        Is("1.528 / 1.500", de, 1528);
        Is("1.234.567", de, 1234567);
        Is("1.528,5", de, 1528.5);
        Is("1,528.5", en, 1528.5);
        Is("1,5K", de, 1500);
        Is("1.5K", en, 1500);
        Is("2.5M", en, 2.5e6);
        Is("0.125", en, 0.125);
        Is("12.5%", en, 12.5);
        Is("12.500%", en, 12.5);
        Is("50", en, 50);
        Is("x 7", en, 7);
        Is("", en, -1);
        Is("none", en, -1);
    }

    public static void TimersInUnitsAreRead()
    {
        void Is(string text, TimeSpan expected)
        {
            var got = Firebot.Utilities.TimeParser.ParseExpectedTime(text) == DateTime.MinValue
                ? TimeSpan.Zero
                : Firebot.Utilities.TimeParser.ParseExpectedTime(text) - DateTime.Now;
            Check(Math.Abs((got - expected).TotalSeconds) < 5, $"'{text}' read as {got}, expected {expected}.");
        }

        Is("03:40:52", new TimeSpan(3, 40, 52));
        Is("2d 03:40:52", new TimeSpan(2, 3, 40, 52));
        Is("6h 59m", new TimeSpan(6, 59, 0));
        Is("6h 59m 30s", new TimeSpan(6, 59, 30));
        Is("12m 5s", new TimeSpan(0, 12, 5));
        Is("2d 3h", new TimeSpan(2, 3, 0, 0));
        Is("Next in 45s", new TimeSpan(0, 0, 45));
        Is("", TimeSpan.Zero);
        Is("soon", TimeSpan.Zero);
    }

    public static void ChaosRiftHolyBubbleBehindRift()
    {
        GameObject.Clear();
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        Logger.Messages.Clear();
        var now = DateTime.Now;
        var task = new ChaosRiftTask(() => now);
        task.InitializeConfig("test");
        task.SetEnabledFromStatus(true);
        var settings = MelonPreferences.Categories["chaos_rift"].Entries;
        ((MelonPreferences_Entry<bool>)settings["buy_eclipse_stones"]).Value = false;
        ((MelonPreferences_Entry<bool>)settings["buy_tomes"]).Value = false;
        ((MelonPreferences_Entry<string>)settings["holy_guardians"]).Value = "";

        var rift = GameObject.CreatePath("menus/ChaosRift");
        rift.activeSelf = false;
        var guardian = GameObject.CreatePath("notifications/GuardianHolyUpgrade");
        var riftBubble = GameObject.CreatePath("notifications/ChaosRift");
        // Opening the rift covers the guardian bubble; closing it shows the bubble again, still lit.
        riftBubble.AddComponent<Button>().onClick.Action = () =>
        {
            rift.activeSelf = true;
            guardian.activeSelf = false;
            riftBubble.activeSelf = false;
        };
        GameObject.CreatePath("menus/ChaosRift/closeButton").AddComponent<Button>().onClick.Action = () =>
        {
            rift.activeSelf = false;
            guardian.activeSelf = true;
        };
        GameObject.CreatePath("menus/ChaosRift/UIElements/godParent/hitButton").AddComponent<Button>();
        Ui.Numbers["menus/ChaosRift/UIElements/counters/currencyInteraction (MoonStone)/quantity"] = 0;
        Ui.Numbers["menus/ChaosRift/UIElements/counters/currencyInteraction (EclipseStone)/quantity"] = 0;

        Check(task.IsNotificationVisible(), "The guardian bubble fixture is not visible.");
        Run(task.Execute());
        Check(guardian.activeSelf && !rift.activeSelf, "The rift fixture did not close with the guardian bubble lit.");
        Check(!task.IsNotificationVisible(),
            "A guardian bubble that was hidden by the rift screen reopened the rift right away.");
        now = now.AddHours(3).AddMinutes(1);
        Check(task.IsNotificationVisible(), "The guardian bubble stayed muted after its three hours.");
    }

    public static void ChaosRiftIdleVisits()
    {
        GameObject.Clear();
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        Logger.Messages.Clear();
        var now = DateTime.Now;
        var task = new ChaosRiftTask(() => now);
        task.InitializeConfig("test");
        task.SetEnabledFromStatus(true);
        var settings = MelonPreferences.Categories["chaos_rift"].Entries;
        ((MelonPreferences_Entry<bool>)settings["buy_eclipse_stones"]).Value = false;
        ((MelonPreferences_Entry<bool>)settings["buy_tomes"]).Value = false;
        ((MelonPreferences_Entry<string>)settings["holy_guardians"]).Value = "";

        var rift = GameObject.CreatePath("menus/ChaosRift");
        rift.activeSelf = false;
        GameObject.CreatePath("notifications/ChaosRift").AddComponent<Button>().onClick.Action =
            () => rift.activeSelf = true;
        GameObject.CreatePath("menus/ChaosRift/closeButton").AddComponent<Button>().onClick.Action =
            () => rift.activeSelf = false;
        GameObject.CreatePath("menus/ChaosRift/UIElements/godParent/hitButton").AddComponent<Button>();
        Ui.Numbers["menus/ChaosRift/UIElements/counters/currencyInteraction (MoonStone)/quantity"] = 0;
        Ui.Numbers["menus/ChaosRift/UIElements/counters/currencyInteraction (EclipseStone)/quantity"] = 0;
        var upgrades = 0;
        GameObject.CreatePath("menus/ChaosRift/UIElements/actionButtons/upgrades").AddComponent<Button>()
            .onClick.Action = () => upgrades++;

        Check(task.IsNotificationVisible(), "The Chaos Rift bubble fixture is not visible.");
        Run(task.Execute());
        Check(!rift.activeSelf && upgrades == 0,
            "An empty Chaos Rift visit opened holy upgrades without their bubble or stayed open.");
        Check(!task.IsNotificationVisible(), "A lit rift bubble with nothing to do reopened the rift right away.");
        Check(task.DueTime > now.AddHours(6), "The rift kept an hourly timer with nothing to hit or buy.");
        now = now.AddHours(3).AddMinutes(1);
        Check(task.IsNotificationVisible(), "The rift stayed muted after its idle wait.");
    }

    public static void TempleChecksWithoutScreen()
    {
        GameObject.Clear();
        Logger.Messages.Clear();
        Il2Cpp.GameInitialize.HandlerLoader = new();
        var battle = Il2Cpp.GameInitialize.HandlerLoader.MainBattle;
        battle.Stage = 500;
        BossAttemptTracker.Reset();
        var task = new TemplePrestigeTask();
        task.InitializeConfig("test");
        task.SetEnabledFromStatus(true);
        var settings = MelonPreferences.Categories["temple_prestige"].Entries;
        ((MelonPreferences_Entry<int>)settings["check_minutes"]).Value = 10;
        ((MelonPreferences_Entry<int>)settings["efficiency_percent"]).Value = 75;
        ((MelonPreferences_Entry<double>)settings["last_gain"]).Value = 50;
        ((MelonPreferences_Entry<string>)settings["last_gain_at"]).Value = DateTime.Now.AddMinutes(-5).ToString("o");
        ((MelonPreferences_Entry<int>)settings["best_stage"]).Value = 0;
        ((MelonPreferences_Entry<string>)settings["best_stage_at"]).Value = "";

        var before = DateTime.Now;
        Check(task.SkipWithoutScreen(), "A check with no trigger close still wanted the temple screen.");
        Check(task.NextRunTime >= before.AddMinutes(9) && task.NextRunTime <= DateTime.Now.AddMinutes(10.1),
            "The temple check did not follow the Check Every setting.");

        ((MelonPreferences_Entry<string>)settings["last_gain_at"]).Value = DateTime.Now.AddMinutes(-58).ToString("o");
        Check(task.SkipWithoutScreen() && task.NextRunTime <= DateTime.Now.AddMinutes(2.1),
            "A longer check interval delayed the hourly growth sample.");

        ((MelonPreferences_Entry<string>)settings["last_gain_at"]).Value = DateTime.Now.AddMinutes(-61).ToString("o");
        Check(!task.SkipWithoutScreen(), "A due growth sample was skipped without opening the temple.");

        ((MelonPreferences_Entry<string>)settings["last_gain_at"]).Value = DateTime.Now.AddMinutes(-5).ToString("o");
        BossAttemptTracker.RecordAttempt(500);
        BossAttemptTracker.RecordAttempt(500);
        BossAttemptTracker.RecordAttempt(500);
        Check(!task.SkipWithoutScreen(), "Reached boss attempts were skipped without opening the temple.");
        BossAttemptTracker.Reset();
        Il2Cpp.GameInitialize.HandlerLoader = null;
    }
}
