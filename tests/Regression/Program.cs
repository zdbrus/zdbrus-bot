using System.Collections;
using Firebot.Behaviors.Extra;
using Firebot.Behaviors.Map;
using Firebot.Behaviors.Town;
using Firebot.BotActions;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Features.Map;
using Firebot.GameModel.Features.Town.Library.FirestoneResearch;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;
using Firebot.UI.Config;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static class Program
{
    private static int _failed;
    private static int _passed;

    private static int Main()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var temp = Path.Combine(Path.GetTempPath(), "ZdbrusBotRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "UserData"));
        try
        {
            Environment.CurrentDirectory = temp;
            BotSettings.Initialize();
            Test("prestige gain parsing fails closed", PrestigeGainParsing);
            Test("prestige run age parsing and reset detection", PrestigeRunAge);
            Test("prestige rate requires consecutive valid samples", PrestigeRate);
            Test("prestige trigger priority favors growth before fallbacks", PrestigeTriggerPriority);
            Test("boss attempts are counted only at the same stage", BossAttemptTracking);
            Test("live stage reads fail closed without reusing old data", LiveStageReading);
            Test("prestige task uses the free control and verifies completion", PrestigeTaskFreeControl);
            Test("prestige confirms a live stage reset with the battle HUD hidden", PrestigeHiddenStageReset);
            Test("prestige rejects missing or changed live stage data", PrestigeLiveStageSafety);
            Test("prestige confirms only its own verified free prompt", PrestigeConfirmationSafety);
            Test("prestige does not mistake an unconfirmed prompt for a reset", PrestigeConfirmationOutcome);
            Test("prestige timer fallback supports the 30-minute option", PrestigeTimerFallback);
            Test("prestige task refuses uncertain or changed game state", PrestigeTaskSafety);
            Test("boss retry threshold can use an already-open free preview", PrestigeBossRetryTrigger);
            Test("prestige task waits for two slow growth samples", PrestigeTaskGrowth);
            Test("arena power, rank and token readings fail closed", ArenaTests.PowerParsing);
            Test("arena selects the weakest valid in-limit opponent", ArenaTests.Selection);
            Test("arena refuses stale, mismatched and changed previews", ArenaTests.PreviewSafety);
            Test("arena daily exception is verified and limited to one attempt", ArenaTests.DailyException);
            Test("arena refreshes only after the list changes", ArenaTests.Refresh);
            Test("arena stops on unconfirmed starts and results", ArenaTests.UnconfirmedBattle);
            Test("arena verifies and restores the configured squad within the task budget", ArenaTests.SquadRestoration);
            Test("arena matchup recording captures verified bot fights only", ArenaTests.MatchupRecording);
            Test("arena recording failures do not change fight decisions", ArenaTests.MatchupRecordingFailures);
            Test("arena matchup files append anonymous JSON records", ArenaTests.MatchupStorage);
            Test("priority tiers hold or retry before advancing", PriorityDecisions);
            Test("battle pass skips scrolling when its reward marker is hidden", BattlePassNoRewardScan);
            Test("liberation difficulty targets and arrow steps are verified", LiberationDifficultySelection);
            Test("scarab permits Pharaoh tokens only with opt-in and verifies exact spending", ScarabSpinSafety);
            Test("talents select live priorities without scanning previews", AutoPlayTests.TalentTargeting);
            Test("talents reject unreadable and changed selections", AutoPlayTests.TalentSafety);
            Test("awakening Auto respects every supported batch size", AutoPlayTests.AwakeningBatches);
            Test("native Auto rejects unconfirmed purchases and releases on cancellation", AutoPlayTests.AutoSafety);
            Test("Scarab Auto enforces token boundaries before native requests", AutoPlayTests.ScarabBoundaries);
            Test("navigation keeps only the compatible hub and never a popup", AutoPlayTests.Navigation);
            Test("uncapped spending modes preserve the player's choice", UncappedUpgradeModes);
            Test("war-machine blueprint priorities preserve order and currency decisions", WarMachineBlueprints);
            Test("update notices distinguish available, current, and failed checks", UpdateNotices);
            Test("updater waits for exit and refuses an early restart", UpdateInstallerSafety);
            Test("exotic upgrades respect locked and affordable priorities", ExoticUpgradeDecisions);
            Test("invalid purchase readings do not confirm progress", PurchaseReadings);
            Test("priority settings preserve order and custom names", OrderedSettings);
            Test("schedule persistence respects actual due time", Scheduling);
            Test("upgrade names persist by tree context", RememberedNames);
            Test("held buttons release and click in order", PointerHold);
            Test("auto-upgrade honors balanced and focused priorities", AutoUpgradeStrategies);
            Test("holy upgrades honor priorities and chosen strategy", HolyUpgradeStrategies);
            Test("map failure retries without false start", MapFailures);
            Test("map successful starts schedule the real timer", MapStart);
            Test("research IDs target verified node order", ResearchIds);
            Test("research handles multiple slots in priority order", ResearchMultipleSlots);
            Test("research refuses stale or failed previews", ResearchPreviewFailures);
            Test("research retries a blocked fallback with another slot running", ResearchBlockedFallback);
            Test("launch opt-in enables only automation switches", AutomationTests.LaunchOverrides);
            Test("scene changes resume workers without undoing manual off", AutomationTests.SceneLifecycle);
            Test("F1 and hotkeys share one automation state", AutomationTests.ToggleState);
            Test("nested coroutine failures and cancellation release resources", AutomationTests.NestedGuard);
            Test("worker failures turn off truthfully and can be retried", AutomationTests.WorkerFailures);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(temp, true);
        }

        Console.WriteLine($"{_passed} passed; {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { _failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Near(double actual, double expected, double tolerance = 0.00001)
        => Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}.");

    private static void Run(IEnumerator routine)
    {
        var steps = 0;
        while (routine.MoveNext())
        {
            if (++steps > 2000) throw new InvalidOperationException("Coroutine did not finish.");
            if (routine.Current is IEnumerator child) Run(child);
            Time.unscaledTime += 0.1f;
        }
    }

    private static void PrestigeGainParsing()
    {
        Check(PrestigePolicy.TryParseGain("+409,340%", out var gain), "Observed preview was rejected.");
        Near(gain, 409340);
        Check(PrestigePolicy.TryParseGain("+1.5K%", out gain), "K suffix was rejected.");
        Near(gain, 1500);
        Check(PrestigePolicy.TryParseGain("+2M%", out gain), "M suffix was rejected.");
        Near(gain, 2000000);
        foreach (var invalid in new[] { "", "1000%", "+15.7cz%", "+1,23%", "+0%", "+NaN%", "+9e99%" })
            Check(!PrestigePolicy.TryParseGain(invalid, out _), $"Accepted {invalid}.");
    }

    private static void PrestigeRunAge()
    {
        Check(PrestigePolicy.TryParseRunHours("07:32:26", out var hours), "Observed run age was rejected.");
        Near(hours, 7 + 32d / 60 + 26d / 3600);
        Check(PrestigePolicy.TryParseRunHours("2d 01:30:00", out hours), "Day notation rejected.");
        Near(hours, 49.5);
        Check(PrestigePolicy.TryParseRunHours("1d 01h 33m 24s", out hours), "Observed long-run age was rejected.");
        Near(hours, 25 + 33d / 60 + 24d / 3600);
        Check(PrestigePolicy.TryParseRunHours("32:00:00", out hours), "Long hour count rejected.");
        Near(hours, 32);
        foreach (var invalid in new[] { "", "1:60:00", "1:00:60", "2d 25:00:00",
                     "1d 24h 00m 00s", "1d 01h 60m 00s", "1d 01h 33m", "unknown" })
            Check(!PrestigePolicy.TryParseRunHours(invalid, out _), $"Accepted {invalid}.");
        Check(PrestigePolicy.IsNewRun(8, 1), "New run was not detected.");
        Check(!PrestigePolicy.IsNewRun(8, 8 - 30d / 3600), "Small clock jitter was treated as a reset.");
        var observedAt = new DateTime(2026, 1, 1, 12, 0, 0);
        Check(PrestigePolicy.RunAgeDiscontinuous(7, 25, observedAt, observedAt.AddHours(1)),
            "A run-age jump consistent with an account switch retained old samples.");
        Check(!PrestigePolicy.RunAgeDiscontinuous(7, 8, observedAt, observedAt.AddHours(1)),
            "Normal run-age growth was treated as a new run.");
        Check(!PrestigePolicy.RunAgeDiscontinuous(7, 7.5, observedAt, observedAt.AddHours(1)),
            "Paused or offline run time was treated as a new run.");
        Check(PrestigePolicy.RunAgeDiscontinuous(7, 8, default, observedAt),
            "Untracked previous run age was trusted.");
    }

    private static void PrestigeRate()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var first = PrestigePolicy.EvaluateRate(1000, 10, -1, default, now, 0, 75);
        Check(first.State == PrestigeRateState.Baseline && first.SaveSample, "Missing baseline.");
        var soon = PrestigePolicy.EvaluateRate(1020, 10.5, 1000, now, now.AddMinutes(30), 1, 75);
        Check(soon.State == PrestigeRateState.TooSoon && !soon.SaveSample && soon.Streak == 1, "Short sample changed streak.");
        var slow = PrestigePolicy.EvaluateRate(1050, 11, 1000, now, now.AddHours(1), 0, 75);
        Check(slow.State == PrestigeRateState.Slow && slow.Streak == 1, "First slow sample triggered or was lost.");
        var trigger = PrestigePolicy.EvaluateRate(1100, 12, 1050, now.AddHours(1), now.AddHours(2), slow.Streak, 75);
        Check(trigger.State == PrestigeRateState.Trigger && trigger.Streak == 2, "Second slow sample did not trigger.");
        var equal = PrestigePolicy.EvaluateRate(1100, 11, 1000, now, now.AddHours(1), 1, 100);
        Check(equal.State == PrestigeRateState.Growing && equal.Streak == 0, "Threshold equality must not trigger.");
        var stale = PrestigePolicy.EvaluateRate(1100, 12, 1000, now, now.AddHours(2).AddSeconds(1), 1, 75);
        Check(stale.State == PrestigeRateState.Baseline && stale.Streak == 0, "Stale sample survived.");
        var lower = PrestigePolicy.EvaluateRate(900, 12, 1000, now, now.AddHours(1), 1, 75);
        Check(lower.State == PrestigeRateState.Baseline, "Decreasing gain did not reset the baseline.");
        var invalid = PrestigePolicy.EvaluateRate(double.NaN, 12, 1000, now, now.AddHours(1), 1, 75);
        Check(invalid.State == PrestigeRateState.Invalid && invalid.Streak == 0, "Invalid gain kept a streak.");
    }

    private static void PrestigeTriggerPriority()
    {
        Check(PrestigePolicy.SelectTrigger(true, true, true) == PrestigeTrigger.Efficiency,
            "Efficiency did not take priority over both fallbacks.");
        Check(PrestigePolicy.SelectTrigger(false, true, true) == PrestigeTrigger.BossRetries,
            "Boss retries did not take priority over the timer fallback.");
        Check(PrestigePolicy.SelectTrigger(false, false, true) == PrestigeTrigger.StallTimer,
            "The stall timer did not remain the final fallback.");
        Check(PrestigePolicy.SelectTrigger(false, false, false) == PrestigeTrigger.None,
            "A missing trigger caused a reset.");
    }

    private static void BossAttemptTracking()
    {
        BossAttemptTracker.Reset();
        Check(BossAttemptTracker.RecordAttempt(1054) == 1, "First boss attempt was not counted.");
        Check(BossAttemptTracker.RecordAttempt(1054) == 2, "Second boss attempt was not counted.");
        Check(BossAttemptTracker.CountAtStage(1054) == 2, "Attempts were not available to the prestige task.");
        Check(BossAttemptTracker.CountAtStage(1055) == 0, "Attempts leaked to a different stage.");
        Check(BossAttemptTracker.RecordAttempt(1055) == 1, "A new stage did not start a fresh attempt count.");
        BossAttemptTracker.Reset();
    }

    private static void LiveStageReading()
    {
        Il2Cpp.GameInitialize.Loaded = true;
        Il2Cpp.GameInitialize.HandlerLoader = new();
        var battle = Il2Cpp.GameInitialize.HandlerLoader.MainBattle;
        battle.Stage = 1054;
        try
        {
            Check(PlayerStats.TryGetCurrentStage(out var stage) && stage == 1054, "Live stage was not read.");
            battle.Stage = 1055;
            Check(PlayerStats.TryGetCurrentStage(out stage) && stage == 1055, "Stage progress was cached.");
            battle.Stage = 1;
            Check(PlayerStats.TryGetCurrentStage(out stage) && stage == 1, "A stage reset was not observed.");
            foreach (var invalid in new[] { 0, -1 })
            {
                battle.Stage = invalid;
                Check(!PlayerStats.TryGetCurrentStage(out stage) && stage == 0, "An invalid stage was accepted.");
            }
            battle.Stage = 1054;
            Il2Cpp.GameInitialize.Loaded = false;
            Check(!PlayerStats.TryGetCurrentStage(out stage) && stage == 0, "An unloaded game reused a stage.");
            Il2Cpp.GameInitialize.Loaded = true;
            Il2Cpp.GameInitialize.HandlerLoader.MainBattle = null;
            Check(!PlayerStats.TryGetCurrentStage(out stage) && stage == 0, "Missing battle data reused a stage.");
            Il2Cpp.GameInitialize.HandlerLoader = null;
            Check(!PlayerStats.TryGetCurrentStage(out stage) && stage == 0, "Missing game data reused a stage.");
            Il2Cpp.GameInitialize.HandlerLoader = new() { MainBattle = battle };
            battle.ThrowOnRead = true;
            var warnings = Logger.Messages.Count;
            Check(!PlayerStats.TryGetCurrentStage(out stage) && stage == 0, "A failed stage read returned stale data.");
            Check(Logger.Messages.Skip(warnings).Any(m => m.Contains("Stage read failed")),
                "A failed stage read was not logged.");
        }
        finally
        {
            Il2Cpp.GameInitialize.Loaded = true;
            Il2Cpp.GameInitialize.HandlerLoader = new();
        }
    }

    private sealed class PrestigeFixture
    {
        public const string Temple = "menus/TempleOfEternals";
        public const string Info = Temple + "/submenus/bgNew/prestigeSubmenu/adventureInfo";
        public const string Stage = "battle/topSideUI/stageProgression/currentStage/stageNum";
        public const string PopupPath = "popups/EmpowerPopup";
        public const string FreePath = PopupPath + "/bg/empowerBg/empowerButton";
        public const string Gain = PopupPath + "/bg/empowerBg/empowerRainingGold/value";
        public const string Age = Info + "/adventureTimePlayed";
        public const string ConfirmationPath = "popups/ActionRequired";
        public const string ConfirmationText = ConfirmationPath + "/bg/messageText";
        public const string ConfirmationButton = ConfirmationPath + "/bg/confirmButton";
        public readonly TemplePrestigeTask Task = new();
        public Dictionary<string, MelonPreferences_Entry> Settings;
        public GameObject TempleScreen;
        public GameObject BattleHud;
        public Il2Cpp.MainBattle Battle;
        public GameObject Popup;
        public GameObject Confirmation;
        public Button FreeButton;
        public int FreeClicks;
        public int PaidClicks;
        public int ConfirmClicks;
        public int PromptClicks;
        public bool NoProgress;
        public bool ShowCompletionPopup = true;
        public bool RequireConfirmation = true;
        public bool ConfirmationNoProgress;
        public Action OnOpen;
        public Action OnConfirmation;
    }

    private static PrestigeFixture SetupPrestige(bool stalled = true)
    {
        GameObject.Clear();
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        Ui.BeforeGuardedClick = null;
        Ui.OpenTownWorks = true;
        BossAttemptTracker.Reset();
        Il2Cpp.GameInitialize.Loaded = true;
        Il2Cpp.GameInitialize.HandlerLoader = new();
        var fixture = new PrestigeFixture();
        fixture.Battle = Il2Cpp.GameInitialize.HandlerLoader.MainBattle;
        fixture.Task.InitializeConfig("test");
        fixture.Settings = MelonPreferences.Categories["temple_prestige"].Entries;
        ((MelonPreferences_Entry<int>)fixture.Settings["stall_minutes"]).Value = 60;
        ((MelonPreferences_Entry<int>)fixture.Settings["boss_attempts"]).Value = 3;
        ((MelonPreferences_Entry<int>)fixture.Settings["min_gain_percent"]).Value = 1000;
        ((MelonPreferences_Entry<int>)fixture.Settings["min_hours_played"]).Value = 1;
        ((MelonPreferences_Entry<int>)fixture.Settings["efficiency_percent"]).Value = 0;
        ((MelonPreferences_Entry<int>)fixture.Settings["best_stage"]).Value = 100;
        ((MelonPreferences_Entry<string>)fixture.Settings["best_stage_at"]).Value =
            DateTime.Now.AddHours(stalled ? -2 : 0).ToString("o");
        ((MelonPreferences_Entry<double>)fixture.Settings["last_observed_run_hours"]).Value =
            25 + 33d / 60 + 24d / 3600;
        ((MelonPreferences_Entry<string>)fixture.Settings["last_observed_run_at"]).Value = DateTime.Now.ToString("o");
        Ui.Numbers[PrestigeFixture.Stage] = 100;
        GameObject.CreatePath(PrestigeFixture.Stage);
        fixture.BattleHud = GameObject.Resolve("battle");
        Ui.Texts[PrestigeFixture.Age] = "1d 01h 33m 24s";
        Ui.Texts[PrestigeFixture.Gain] = "+14,159%";
        fixture.TempleScreen = GameObject.CreatePath(PrestigeFixture.Temple);
        fixture.TempleScreen.activeSelf = false;
        var open = GameObject.CreatePath(PrestigeFixture.Info + "/openEmpowerButton").AddComponent<Button>();
        GameObject.CreatePath(PrestigeFixture.Temple + "/closeButton").AddComponent<Button>().onClick.Action =
            () => fixture.TempleScreen.activeSelf = false;
        fixture.Popup = GameObject.CreatePath(PrestigeFixture.PopupPath);
        fixture.Popup.activeSelf = false;
        open.onClick.Action = () =>
        {
            fixture.Popup.activeSelf = true;
            fixture.BattleHud.activeSelf = false;
            fixture.OnOpen?.Invoke();
        };
        GameObject.CreatePath(PrestigeFixture.PopupPath + "/bg/closeButton").AddComponent<Button>().onClick.Action =
            () => fixture.Popup.activeSelf = false;
        var complete = GameObject.CreatePath("popups/TOEPrestigeComplete");
        complete.activeSelf = false;
        GameObject.CreatePath("popups/TOEPrestigeComplete/bg/confirmButton").AddComponent<Button>().onClick.Action =
            () => { fixture.ConfirmClicks++; complete.activeSelf = false; };
        fixture.Confirmation = GameObject.CreatePath(PrestigeFixture.ConfirmationPath);
        fixture.Confirmation.activeSelf = false;
        Ui.Texts[PrestigeFixture.ConfirmationText] = "Are you sure that you want to empower the temple?";
        void FinishPrestige()
        {
            fixture.Battle.Stage = 1;
            Ui.Numbers[PrestigeFixture.Stage] = 1;
            fixture.Popup.activeSelf = false;
            fixture.Confirmation.activeSelf = false;
            complete.activeSelf = fixture.ShowCompletionPopup;
        }
        GameObject.CreatePath(PrestigeFixture.ConfirmationButton).AddComponent<Button>().onClick.Action = () =>
        {
            fixture.PromptClicks++;
            if (!fixture.ConfirmationNoProgress) FinishPrestige();
        };
        fixture.FreeButton = GameObject.CreatePath(PrestigeFixture.FreePath).AddComponent<Button>();
        fixture.FreeButton.onClick.Action = () =>
        {
            fixture.FreeClicks++;
            if (fixture.NoProgress) return;
            if (fixture.RequireConfirmation)
            {
                fixture.Popup.activeSelf = false;
                fixture.Confirmation.activeSelf = true;
                fixture.OnConfirmation?.Invoke();
            }
            else FinishPrestige();
        };
        GameObject.CreatePath(PrestigeFixture.PopupPath + "/bg/empowerBg/empower2x/empowerButton")
            .AddComponent<Button>().onClick.Action = () => fixture.PaidClicks++;
        return fixture;
    }

    private static void PrestigeTaskFreeControl()
    {
        var fixture = SetupPrestige();
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 1 && fixture.PaidClicks == 0 && fixture.PromptClicks == 1 && fixture.ConfirmClicks == 1,
            "The verified free prestige flow was not followed exactly once.");
        Check(TemplePrestigeTask.LastDecision.Contains("Prestige confirmed"),
            "A confirmed prestige was not reported.");
    }

    private static void PrestigeHiddenStageReset()
    {
        var fixture = SetupPrestige();
        fixture.ShowCompletionPopup = false;
        Run(fixture.Task.Execute());
        Check(Ui.NumberAt(PrestigeFixture.Stage) == -1, "The fixture did not hide the battle HUD.");
        Check(fixture.FreeClicks == 1 && fixture.PaidClicks == 0 && fixture.ConfirmClicks == 0,
            "The free stage reset needed a visible HUD or clicked an unrelated control.");
        Check(TemplePrestigeTask.LastDecision.Contains("Prestige confirmed"),
            "The live stage reset was not confirmed.");
    }

    private static void PrestigeConfirmationSafety()
    {
        var fixture = SetupPrestige();
        fixture.Confirmation.activeSelf = true;
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0 && fixture.PromptClicks == 0 && fixture.Confirmation.activeSelf,
            "A pre-existing confirmation was claimed by the task.");

        fixture = SetupPrestige();
        Ui.Texts[PrestigeFixture.ConfirmationText] = "Spend gems?";
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 1 && fixture.PromptClicks == 0 && fixture.PaidClicks == 0,
            "An unrelated prompt was confirmed.");

        fixture = SetupPrestige();
        GameObject.CreatePath(PrestigeFixture.ConfirmationButton + "/gemCost");
        Run(fixture.Task.Execute());
        Check(fixture.PromptClicks == 0, "A confirmation showing a price was clicked.");

        fixture = SetupPrestige();
        var battle = fixture.Battle;
        fixture.OnConfirmation = () => battle.Stage = 101;
        Run(fixture.Task.Execute());
        Check(fixture.PromptClicks == 0, "New stage progress did not cancel the pending reset.");

        fixture = SetupPrestige();
        Ui.BeforeGuardedClick = path =>
        {
            if (path == PrestigeFixture.ConfirmationButton)
                Ui.Texts[PrestigeFixture.ConfirmationText] = "An unrelated confirmation";
        };
        try
        {
            Run(fixture.Task.Execute());
            Check(fixture.PromptClicks == 0, "A swapped confirmation was clicked.");
        }
        finally { Ui.BeforeGuardedClick = null; }
    }

    private static void PrestigeConfirmationOutcome()
    {
        var fixture = SetupPrestige();
        fixture.ConfirmationNoProgress = true;
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 1 && fixture.PromptClicks == 1 && fixture.ConfirmClicks == 0 &&
              TemplePrestigeTask.LastDecision.Contains("not confirmed"),
            "A confirmation click without a reset was reported as success or retried.");
        fixture = SetupPrestige();
        fixture.RequireConfirmation = false;
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 1 && fixture.PromptClicks == 0 && fixture.ConfirmClicks == 1,
            "A direct free reset incorrectly required an extra prompt.");
    }

    private static void PrestigeLiveStageSafety()
    {
        var fixture = SetupPrestige();
        Il2Cpp.GameInitialize.Loaded = false;
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0 && TemplePrestigeTask.LastDecision.Contains("Stage unreadable"),
            "An unloaded game used a stale stage.");

        foreach (var current in new[] { 0, 1, 101 })
        {
            fixture = SetupPrestige();
            var battle = fixture.Battle;
            fixture.OnOpen = () => battle.Stage = current;
            Run(fixture.Task.Execute());
            Check(fixture.FreeClicks == 0 && fixture.PaidClicks == 0,
                $"A changed or invalid live stage ({current}) allowed prestige.");
        }

        fixture = SetupPrestige();
        fixture.OnOpen = () => Il2Cpp.GameInitialize.HandlerLoader.MainBattle = null;
        var messages = Logger.Messages.Count;
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0 && fixture.PaidClicks == 0,
            "Missing live stage data during the preview allowed prestige.");
        Check(Logger.Messages.Skip(messages).Any(m => m.Contains("Stage 100 -> unavailable")),
            "The final guard did not identify the missing stage reading.");
    }

    private static void PrestigeTimerFallback()
    {
        var fixture = SetupPrestige(stalled: false);
        ((MelonPreferences_Entry<int>)fixture.Settings["stall_minutes"]).Value = 30;
        ((MelonPreferences_Entry<string>)fixture.Settings["best_stage_at"]).Value =
            DateTime.Now.AddMinutes(-29).ToString("o");
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0, "The 30-minute fallback triggered before its delay elapsed.");

        ((MelonPreferences_Entry<string>)fixture.Settings["best_stage_at"]).Value =
            DateTime.Now.AddMinutes(-31).ToString("o");
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 1 && fixture.ConfirmClicks == 1,
            "The 30-minute fallback did not trigger after the delay elapsed.");
    }

    private static void PrestigeTaskSafety()
    {
        var fixture = SetupPrestige();
        Ui.Texts[PrestigeFixture.Gain] = "+15.7cz%";
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0 && fixture.PaidClicks == 0, "An unsupported boost triggered prestige.");

        fixture = SetupPrestige();
        Ui.Texts[PrestigeFixture.Age] = "unknown";
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0, "An unreadable run age triggered prestige.");

        fixture = SetupPrestige(stalled: false);
        fixture.TempleScreen.activeSelf = true;
        fixture.Popup.activeSelf = true;
        Run(fixture.Task.Execute());
        Check(fixture.Popup.activeSelf && fixture.FreeClicks == 0, "An existing preview was modified.");

        fixture = SetupPrestige();
        GameObject.CreatePath(PrestigeFixture.FreePath + "/gemCost");
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0 && fixture.PaidClicks == 0, "A priced control was used.");

        fixture = SetupPrestige();
        fixture.FreeButton.interactable = false;
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0, "A disabled free control was used.");

        fixture = SetupPrestige();
        fixture.OnOpen = () =>
        {
            fixture.Battle.Stage = 101;
            Ui.Numbers[PrestigeFixture.Stage] = 101;
        };
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0, "Progress during the check did not cancel a stalled-run prestige.");

        fixture = SetupPrestige();
        ((MelonPreferences_Entry<double>)fixture.Settings["last_observed_run_hours"]).Value = 30;
        ((MelonPreferences_Entry<string>)fixture.Settings["last_observed_run_at"]).Value =
            DateTime.Now.AddMinutes(-5).ToString("o");
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0, "A changed run inherited the previous run's stall timer.");

        fixture = SetupPrestige();
        ((MelonPreferences_Entry<double>)fixture.Settings["last_observed_run_hours"]).Value = -1;
        ((MelonPreferences_Entry<string>)fixture.Settings["last_observed_run_at"]).Value = "";
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0, "An untracked run inherited an old stall timer.");

        fixture = SetupPrestige();
        fixture.NoProgress = true;
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 1 && fixture.ConfirmClicks == 0 && fixture.PaidClicks == 0 &&
              TemplePrestigeTask.LastDecision.Contains("not confirmed"),
            "Unconfirmed prestige clicked another control or reported success.");
    }

    private static void PrestigeBossRetryTrigger()
    {
        var fixture = SetupPrestige(stalled: false);
        ((MelonPreferences_Entry<int>)fixture.Settings["boss_attempts"]).Value = 2;
        fixture.TempleScreen.activeSelf = true;
        fixture.Popup.activeSelf = true;
        fixture.BattleHud.activeSelf = false;
        BossAttemptTracker.RecordAttempt(100);
        BossAttemptTracker.RecordAttempt(100);
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 1 && fixture.PaidClicks == 0 && fixture.ConfirmClicks == 1,
            "The boss retry trigger did not use the already-open free preview.");
        Check(TemplePrestigeTask.LastDecision.Contains("Prestige confirmed"),
            "Boss-triggered prestige was not confirmed.");
    }

    private static void PrestigeTaskGrowth()
    {
        var fixture = SetupPrestige(stalled: false);
        ((MelonPreferences_Entry<int>)fixture.Settings["efficiency_percent"]).Value = 75;
        Ui.Texts[PrestigeFixture.Age] = "1d 00h 00m 00s";
        Ui.Texts[PrestigeFixture.Gain] = "+10,000%";
        Run(fixture.Task.Execute());
        Check(fixture.FreeClicks == 0 && TemplePrestigeTask.LastDecision.Contains("First growth sample"),
            "A baseline triggered prestige.");

        for (var sample = 1; sample <= 2; sample++)
        {
            ((MelonPreferences_Entry<string>)fixture.Settings["last_gain_at"]).Value =
                DateTime.Now.AddHours(-1).AddSeconds(-1).ToString("o");
            ((MelonPreferences_Entry<string>)fixture.Settings["last_observed_run_at"]).Value =
                DateTime.Now.AddHours(-1).AddSeconds(-1).ToString("o");
            Ui.Texts[PrestigeFixture.Age] = $"1d 0{sample}h 00m 00s";
            Ui.Texts[PrestigeFixture.Gain] = $"+{10000 + sample * 10:N0}%";
            Run(fixture.Task.Execute());
            Check(fixture.FreeClicks == (sample == 2 ? 1 : 0),
                "The growth trigger did not require two consecutive slow hourly samples.");
        }
        Check(fixture.PaidClicks == 0 && fixture.ConfirmClicks == 1,
            "The growth trigger did not use the verified free completion flow.");
    }

    private static void ScarabSpinSafety()
    {
        Check(ScarabSpinPolicy.SelectCurrency(true, false, false) == ScarabSpinCurrency.Noble,
            "Noble spins were blocked.");
        Check(ScarabSpinPolicy.SelectCurrency(false, true, false) == ScarabSpinCurrency.None,
            "Pharaoh tokens were allowed without opt-in.");
        Check(ScarabSpinPolicy.SelectCurrency(false, true, true) == ScarabSpinCurrency.Pharaoh,
            "Opted-in Pharaoh spins were blocked.");
        foreach (var allowed in new[] { false, true })
        {
            Check(ScarabSpinPolicy.SelectCurrency(true, true, allowed) == ScarabSpinCurrency.None &&
                  ScarabSpinPolicy.SelectCurrency(false, false, allowed) == ScarabSpinCurrency.None,
                "An ambiguous or unknown token type was allowed.");
        }
        Check(ScarabSpinPolicy.CanSpin(ScarabSpinCurrency.Pharaoh, 9, 1),
            "The observed affordable Pharaoh spin was rejected.");
        foreach (var balance in new[] { -1d, 0, 0.5, double.NaN, double.PositiveInfinity })
            Check(!ScarabSpinPolicy.CanSpin(ScarabSpinCurrency.Pharaoh, balance, 1),
                "An unreadable, fractional, or insufficient balance was accepted.");
        foreach (var cost in new[] { -1d, 0, 0.5, double.NaN, double.PositiveInfinity })
            Check(!ScarabSpinPolicy.CanSpin(ScarabSpinCurrency.Pharaoh, 9, cost),
                "An invalid cost was accepted.");
        Check(!ScarabSpinPolicy.CanSpin(ScarabSpinCurrency.None, 9, 1), "An unapproved currency was accepted.");
        Check(ScarabSpinPolicy.SpentExactly(ScarabSpinCurrency.Pharaoh, 9, 8, 1), "Exact token use was not confirmed.");
        foreach (var after in new[] { -1d, 7, 9, double.NaN })
            Check(!ScarabSpinPolicy.SpentExactly(ScarabSpinCurrency.Pharaoh, 9, after, 1),
                "Unreadable, unchanged, or unexpected token use was reported as a confirmed spin.");
    }

    private static void BattlePassNoRewardScan()
    {
        Check(!BattlePassPolicy.ShouldScanRewards(notificationFound: true, notificationVisible: false),
            "A hidden rewards marker still scanned the reward track.");
        Check(BattlePassPolicy.ShouldScanRewards(notificationFound: true, notificationVisible: true),
            "A visible rewards marker skipped the reward scan.");
        Check(BattlePassPolicy.ShouldScanRewards(notificationFound: false, notificationVisible: false),
            "A missing rewards marker incorrectly skipped the conservative scan.");
    }

    private static void LiberationDifficultySelection()
    {
        Check(LiberationDifficultyPolicy.TryGetTargets("Easy", out var easy) &&
              easy.Length == 1 && easy[0] == 1, "Easy did not target one star.");
        Check(LiberationDifficultyPolicy.TryGetTargets("Normal", out var normal) &&
              normal.Length == 1 && normal[0] == 2, "Normal did not target two stars.");
        Check(LiberationDifficultyPolicy.TryGetTargets("Both", out var both) &&
              both.Length == 2 && both[0] == 1 && both[1] == 2, "Both did not target Easy then Normal.");
        Check(!LiberationDifficultyPolicy.TryGetTargets("Medium", out _), "An unsupported difficulty was accepted.");
        Check(LiberationDifficultyPolicy.TryGetStep(1, 2, out var forward) && forward == 1,
            "The selector did not advance from one to two stars.");
        Check(LiberationDifficultyPolicy.TryGetStep(2, 1, out var backward) && backward == -1,
            "The selector did not retreat from two to one star.");
        Check(!LiberationDifficultyPolicy.TryGetStep(3, 1, out _),
            "An unsupported star count was accepted.");
    }

    private static void PriorityDecisions()
    {
        var order = new[] { "Prestigious", "Raining gold", "Damage" };
        var sorted = new[] { "Damage", "raining GOLD", "Other", " PRESTIGIOUS " }
            .OrderBy(name => PriorityTier.Rank(order, name)).ToArray();
        Check(sorted.SequenceEqual(new[] { " PRESTIGIOUS ", "raining GOLD", "Damage", "Other" }), "Priority order changed.");
        Check(PriorityTier.AfterPass(true, true, false, false, 0, 0) == PriorityTierAction.Advance, "Maxed tier did not advance.");
        Check(PriorityTier.AfterPass(false, false, false, false, 0, 0) == PriorityTierAction.Advance, "Unavailable tier did not advance.");
        Check(PriorityTier.AfterPass(false, true, false, false, 100, 50) == PriorityTierAction.Wait, "Unaffordable tier advanced.");
        Check(PriorityTier.AfterPass(false, true, true, true, 10, 50) == PriorityTierAction.RetrySoon, "Affordable capped tier did not retry.");
        Check(PriorityTier.AfterPass(false, true, true, true, -1, 50) == PriorityTierAction.Wait, "Unreadable price advanced.");
    }

    private static void PurchaseReadings()
    {
        Check(PurchaseObservation.LevelIncreased("Level 8/10", "Level 9/10"), "Valid level increase missed.");
        Check(!PurchaseObservation.LevelIncreased("Level 8/10", ""), "Blank level confirmed a purchase.");
        Check(!PurchaseObservation.LevelIncreased("Level 8/10", "Level 7/10"), "Decreasing level confirmed a purchase.");
        Check(!PurchaseObservation.ChangedPrice(100, -1), "Unreadable price confirmed a purchase.");
        Check(PurchaseObservation.ChangedPrice(100, 150), "Changed price not observed.");
        Check(!PurchaseObservation.Decreased(100, -1), "Unreadable currency confirmed spending.");
        Check(PurchaseObservation.Decreased(100, 0), "Spending the final currency was missed.");
        Check(!PurchaseObservation.Increased(-1, 20), "Unreadable initial currency confirmed a sale.");
    }

    private static void UncappedUpgradeModes()
    {
        Check(UncappedUpgradePolicy.TryParse("balanced", out var balanced) && balanced == UncappedUpgradeMode.Balanced,
            "Balanced strategy was not recognized.");
        Check(UncappedUpgradePolicy.ContinueAfterTarget(balanced, true),
            "Balanced strategy did not consider the next selected target.");
        Check(UncappedUpgradePolicy.TryParse("Focus", out var focus) && focus == UncappedUpgradeMode.Focus,
            "Focus strategy was not recognized.");
        Check(!UncappedUpgradePolicy.ContinueAfterTarget(focus, true),
            "Focus strategy fell through an available target.");
        Check(UncappedUpgradePolicy.ContinueAfterTarget(focus, false),
            "Focus strategy did not skip an unavailable target.");
        Check(!UncappedUpgradePolicy.TryParse("invalid", out _), "Invalid strategy silently defaulted.");
    }

    private static void WarMachineBlueprints()
    {
        Check(WarMachineBlueprintPolicy.TryGetSlots("all", "armor,damage,health", out var slots) &&
              slots.SequenceEqual(new[] { 2, 0, 1 }), "Blueprint priority order was not preserved.");
        Check(WarMachineBlueprintPolicy.TryGetSlots("all", "", out slots) && slots.Length == 0,
            "An empty blueprint selection spent currency.");
        Check(WarMachineBlueprintPolicy.TryGetSlots("health", "armor,damage", out slots) &&
              slots.SequenceEqual(new[] { 1 }), "A single blueprint mode was affected by the all-mode order.");
        Check(!WarMachineBlueprintPolicy.TryGetSlots("all", "damage,unknown", out _),
            "An invalid blueprint priority was accepted.");
        Check(!WarMachineBlueprintPolicy.TryGetSlots("unknown", "damage", out _),
            "An invalid blueprint mode was accepted.");
        Check(WarMachineBlueprintPolicy.Combine(PriorityTierAction.Wait, PriorityTierAction.Advance) == PriorityTierAction.Wait,
            "A waiting level tier was lost after a completed blueprint tier.");
        Check(WarMachineBlueprintPolicy.Combine(PriorityTierAction.Wait, PriorityTierAction.RetrySoon) == PriorityTierAction.RetrySoon,
            "A retryable tier was delayed by an unrelated currency wait.");
    }

    private static void UpdateNotices()
    {
        Check(UpdateStatusPolicy.Classify("0.7.38", "v0.7.39", 0, out var latest) == UpdateStatus.Available &&
              latest == "0.7.39", "A newer release was not detected.");
        var notice = UpdateStatusPolicy.Describe(UpdateStatus.Available, latest, "0.7.38");
        Check(notice.Contains("Close Firestone") && notice.Contains("wait") && notice.Contains("reopen"),
            "The available-update notice did not explain the installation sequence.");
        Check(UpdateStatusPolicy.Classify("0.7.38", "v0.7.38", 0, out latest) == UpdateStatus.Current &&
              latest == null, "The installed release was reported as newer.");
        Check(UpdateStatusPolicy.Classify("0.7.38", "v0.7.37", 0, out _) == UpdateStatus.Current,
            "An older release was reported as an update.");
        Check(UpdateStatusPolicy.Classify("0.7.38", "bad-tag", 0, out _) == UpdateStatus.Failed &&
              UpdateStatusPolicy.Classify("0.7.38", "v0.7.39", 1, out _) == UpdateStatus.Failed,
            "A failed or invalid check was reported as successful.");
        Check(UpdateStatusPolicy.Describe(UpdateStatus.Disabled, null, "0.7.38").Contains("off") &&
              UpdateStatusPolicy.Describe(UpdateStatus.Failed, null, "0.7.38").Contains("failed"),
            "Disabled or failed checks gave a misleading status.");
    }

    private static void UpdateInstallerSafety()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Environment.CurrentDirectory, "updater-fixtures");
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "updater.ps1");
        File.WriteAllText(script, UpdateCheck.InstallerScript);
        var wrapper = Path.Combine(root, "exercise.ps1");
        File.WriteAllText(wrapper, @"
param([string]$Work, [string]$Mode, [string]$Script)
$ErrorActionPreference = 'Stop'
$env:TEMP = $Work
$network = Join-Path $Work 'network.txt'
function Wait-Process {
    param([int]$Id, [string]$ErrorAction)
    if ($Mode -eq 'blocked') { throw 'wait blocked' }
}
function Get-Process {
    param([int]$Id, [string]$Name, [string]$ErrorAction)
    if (($Mode -eq 'blocked' -and $Id -eq 91234) -or ($Mode -eq 'restarted' -and $Name -eq 'FakeFirestone')) {
        [pscustomobject]@{ Id = 91234 }
    }
}
function Invoke-RestMethod {
    param($Uri, $Headers, $TimeoutSec)
    [IO.File]::WriteAllText($network, 'called')
    [pscustomobject]@{ tag_name = 'v0.7.39'; assets = @([pscustomobject]@{ name = 'ZdbrusBot.zip'; browser_download_url = 'unused' }) }
}
function Invoke-WebRequest {
    param($Uri, $OutFile, $Headers, $TimeoutSec, [switch]$UseBasicParsing)
    [IO.File]::WriteAllText($OutFile, 'archive')
}
function Expand-Archive {
    param($Path, $DestinationPath, [switch]$Force)
    $mods = Join-Path $DestinationPath 'Mods'
    [IO.Directory]::CreateDirectory($mods) | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $mods 'ZdbrusBot.dll'), (New-Object byte[] 100001))
}
$dll = Join-Path $Work 'old.dll'
$log = Join-Path $Work 'update.log'
[IO.File]::WriteAllText($dll, 'original')
& $Script -GamePid 91234 -GameName 'FakeFirestone' -Dll $dll -Current '0.7.38' -LogFile $log
$result = [IO.File]::ReadAllText($log)
if ($Mode -eq 'blocked') {
    if ((Test-Path $network) -or $result -notmatch 'Update failed' -or [IO.File]::ReadAllText($dll) -ne 'original') { exit 2 }
} elseif ($Mode -eq 'restarted') {
    if ($result -notmatch 'restarted before the update finished' -or [IO.File]::ReadAllText($dll) -ne 'original') { exit 3 }
} else {
    if ($result -notmatch 'Updated 0.7.38 -> 0.7.39' -or (Get-Item $dll).Length -ne 100001 -or
        [IO.File]::ReadAllText($dll + '.bak') -ne 'original') { exit 4 }
}
");
        foreach (var mode in new[] { "blocked", "restarted", "success" })
        {
            var work = Path.Combine(root, mode);
            Directory.CreateDirectory(work);
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            var psi = new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", wrapper,
                         "-Work", work, "-Mode", mode, "-Script", script })
                psi.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(psi);
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new InvalidOperationException($"Updater fixture {mode} timed out.");
            }
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Check(process.ExitCode == 0, $"Updater fixture {mode} failed: {output}");
        }
    }

    private static void ExoticUpgradeDecisions()
    {
        var order = new[] { "Prestigious", "Raining gold", "Skip stage", "Skip wave" };
        var ranked = new[] { "Skip wave 1000-1200", "Raining gold", "Skip stage 1000-1200", "Prestigious" }
            .OrderBy(name => ExoticUpgradePolicy.Rank(order, name)).ToArray();
        Check(ranked.SequenceEqual(new[] { "Prestigious", "Raining gold", "Skip stage 1000-1200", "Skip wave 1000-1200" }),
            "Exotic priority order changed.");
        Check(ExoticUpgradePolicy.Decide(true, false, false, false, false, -1, 0) == ExoticUpgradeAction.Advance,
            "Maxed upgrade did not advance.");
        Check(ExoticUpgradePolicy.Decide(false, true, false, false, false, -1, 0) == ExoticUpgradeAction.Advance,
            "Level-locked upgrade blocked available tiers.");
        Check(ExoticUpgradePolicy.Decide(false, false, true, false, false, 100, 50) == ExoticUpgradeAction.Wait,
            "Unaffordable upgrade advanced.");
        Check(ExoticUpgradePolicy.Decide(false, false, false, false, false, 100, 100) == ExoticUpgradeAction.Wait,
            "Missing control advanced.");
        Check(ExoticUpgradePolicy.Decide(false, false, true, true, false, 100, 100) == ExoticUpgradeAction.Buy,
            "Affordable upgrade was not bought.");
        Check(ExoticUpgradePolicy.Decide(false, false, true, true, true, 100, 100) == ExoticUpgradeAction.RetrySoon,
            "Purchase cap advanced instead of retrying.");
        Check(ExoticUpgradePolicy.Decide(false, false, true, true, false, double.NaN, 100) == ExoticUpgradeAction.Wait,
            "Unreadable price was accepted.");
    }

    private static void OrderedSettings()
    {
        var category = new MelonPreferences_Category { Identifier = "personal_tree" };
        var entry = category.CreateEntry("priority", "Damage,Custom bonus,Gold", "Priority", "");
        var choices = new[] { new ConfigChoice("Gold", "Gold"), new ConfigChoice("Damage", "Damage"), new ConfigChoice("Health", "Health") };
        var config = new ConfigEntry(category, entry, default, new ChoiceSet(choices, true));
        var selected = config.ReadSelection();
        Check(selected.SequenceEqual(new[] { "Damage", "Custom bonus", "Gold" }), "Existing order or custom priority was lost.");
        selected.Remove("Gold");
        selected.Add("Health");
        Check(config.EncodeSelection(selected) == "Damage,Custom bonus,Health", "Edited priorities were reordered.");

        var ordinaryCategory = new MelonPreferences_Category { Identifier = "alchemist" };
        var ordinary = ordinaryCategory.CreateEntry("resources", "Damage,Gold", "Resources", "");
        var regular = new ConfigEntry(ordinaryCategory, ordinary, default, new ChoiceSet(choices, true));
        Check(regular.EncodeSelection(regular.ReadSelection()) == "Gold,Damage", "Ordinary multi-select behavior changed.");

        var exoticCategory = new MelonPreferences_Category { Identifier = "exotic_merchant" };
        var exotic = exoticCategory.CreateEntry("priority", "Damage,Gold", "Priority", "");
        var exoticConfig = new ConfigEntry(exoticCategory, exotic, default, new ChoiceSet(choices, true));
        Check(exoticConfig.EncodeSelection(exoticConfig.ReadSelection()) == "Damage,Gold", "Exotic priority was reordered.");

        var guardians = new MelonPreferences_Category { Identifier = "chaos_rift" };
        var holy = guardians.CreateEntry("holy_guardians", "Damage,Gold", "Guardians", "");
        var holyConfig = new ConfigEntry(guardians, holy, default, new ChoiceSet(choices, true));
        Check(holyConfig.EncodeSelection(holyConfig.ReadSelection()) == "Damage,Gold", "Guardian priority was reordered.");

        var auto = new MelonPreferences_Category { Identifier = "auto_upgrade" };
        var slots = auto.CreateEntry("upgrade_target_slots", "Damage,Gold", "Slots", "");
        var slotConfig = new ConfigEntry(auto, slots, default, new ChoiceSet(choices, true));
        Check(slotConfig.EncodeSelection(slotConfig.ReadSelection()) == "Damage,Gold", "AutoUpgrade priority was reordered.");

        var machines = new MelonPreferences_Category { Identifier = "war_machine_upgrade" };
        var blueprints = machines.CreateEntry("blueprint_priority", "Damage,Gold", "Blueprints", "");
        var blueprintConfig = new ConfigEntry(machines, blueprints, default, new ChoiceSet(choices, true));
        Check(blueprintConfig.EncodeSelection(blueprintConfig.ReadSelection()) == "Damage,Gold", "Blueprint priority was reordered.");
    }

    private static void AutoUpgradeStrategies()
    {
        GameObject.Clear();
        AutoUpgrade.Initialize();
        var settings = MelonPreferences.Categories["auto_upgrade"].Entries;
        var mode = (MelonPreferences_Entry<string>)settings["priority_mode"];
        var order = (MelonPreferences_Entry<string>)settings["upgrade_target_slots"];
        Check(mode.Value == "balanced", "Existing players did not retain balanced spending.");
        order.Value = "2,0";
        const string list = Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.UpgradesList;
        var special = GameObject.CreatePath(list + "/customUpgrade/buyButton").AddComponent<Button>();
        GameObject.CreatePath(list + "/guardianSlot/lvlUpButton").AddComponent<Button>();
        var hero = GameObject.CreatePath(list + "/heroSlot/lvlUpButton").AddComponent<Button>();
        var cycle = typeof(AutoUpgrade).GetMethod("UpgradeCycle",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check(cycle != null, "AutoUpgrade cycle was not found.");

        void RunCycle(string strategy)
        {
            mode.Value = strategy;
            ExecuteEvents.Targets.Clear();
            Run((IEnumerator)cycle.Invoke(null, null));
        }

        mode.Value = "focus";
        RunCycle("focus");
        Check(ExecuteEvents.Targets.Count == 20 &&
              ExecuteEvents.Targets.All(path => path.EndsWith("/heroSlot/lvlUpButton")),
            "Focused spending fell through or did not finish its bounded burst.");

        hero.interactable = false;
        RunCycle("focus");
        Check(ExecuteEvents.Targets.Count == 0, "Focused spending fell through an unaffordable top slot.");

        RunCycle("balanced");
        Check(ExecuteEvents.Targets.Count == 1 && ExecuteEvents.Targets[0].EndsWith("/customUpgrade/buyButton"),
            "Balanced spending did not continue to an affordable lower slot.");

        hero.interactable = true;
        RunCycle("balanced");
        Check(ExecuteEvents.Targets.SequenceEqual(new[]
            { list + "/heroSlot/lvlUpButton", list + "/customUpgrade/buyButton" }),
            "Balanced spending ignored the chosen order.");

        order.Value = "2,1,0";
        BotManager.PauseAutoUpgrade = false;
        ExecuteEvents.OnDown = _ =>
        {
            Check(AutoUpgrade.IsBalancedCycleActive, "Balanced cycle was not held open through its button pass.");
            BotManager.PauseAutoUpgrade = true;
        };
        RunCycle("balanced");
        Check(ExecuteEvents.Targets.SequenceEqual(new[]
            { list + "/heroSlot/lvlUpButton", list + "/guardianSlot/lvlUpButton",
                list + "/customUpgrade/buyButton" }),
            "An approaching task interrupted the balanced pass after its first target.");
        Check(Logger.Messages.Any(message => message.Contains("Balanced pass finished")),
            "The completed handoff pass was not reported.");
        ExecuteEvents.OnDown = null;
        BotManager.PauseAutoUpgrade = false;

        BotManager.IsTaskExecuting = true;
        RunCycle("balanced");
        Check(ExecuteEvents.Targets.Count == 0, "A balanced pass continued after the task had started.");
        BotManager.IsTaskExecuting = false;

        RunCycle("invalid");
        Check(ExecuteEvents.Targets.Count == 0, "Invalid strategy spent currency.");
        Check(special.interactable, "Fixture changed the lower-priority button.");
    }

    private sealed class HolyFixture
    {
        public readonly List<string> Selected = new();
        public readonly List<string> Bought = new();
        public readonly Dictionary<string, double> Costs = new() { ["Vermilion"] = 100, ["Grace"] = 10 };
        public readonly Dictionary<string, Il2Cpp.Guardian> Guardians = new();
        public bool WrongPreview;
        public bool NoProgress;
        public bool Retry;
        public string Current;
        public int Level;
    }

    private static HolyFixture SetupHoly(double balance)
    {
        GameObject.Clear();
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        const string mq = "menus/MagicQuarters";
        const string holy = mq + "/submenus/bg/chaosRiftSubmenu/holyDamageUpgrade/bg/upgradeButton";
        const string orbs = mq + "/counters/currencyInteraction (OrbOfLight)/quantity";
        const string namePath = mq + "/guardianPreview/info/guardianName";
        const string levelPath = mq + "/submenus/bg/chaosRiftSubmenu/holyDamageUpgrade/bg/upgradeNumBg/upgradeNum";
        var fixture = new HolyFixture();
        var highlights = new List<GameObject>();
        foreach (var name in new[] { "Grace", "Vermilion" })
        {
            var tile = GameObject.CreatePath(mq + "/guardianList/" + name);
            var highlight = GameObject.CreatePath(mq + "/guardianList/" + name + "/highlighted");
            highlight.activeSelf = false;
            highlights.Add(highlight);
            var guardian = new Il2Cpp.Guardian { guardianName = name };
            guardian.IsUnlocked.Value = true;
            fixture.Guardians[name] = guardian;
            tile.AddComponent<Il2Cpp.GuardianInteractionSquare>().guardian = guardian;
            tile.AddComponent<Button>().onClick.Action = () =>
            {
                foreach (var marker in highlights) marker.activeSelf = false;
                highlight.activeSelf = true;
                fixture.Current = name;
                fixture.Selected.Add(name);
                Ui.Texts[namePath] = fixture.WrongPreview ? "Other" : name;
                Ui.Numbers[holy + "/layout/costText"] = fixture.Costs[name];
                Ui.Texts[levelPath] = fixture.Level.ToString();
            };
        }
        var button = GameObject.CreatePath(holy).AddComponent<Button>();
        button.onClick.Action = () =>
        {
            if (fixture.NoProgress) return;
            Ui.Numbers[orbs] -= fixture.Costs[fixture.Current];
            fixture.Bought.Add(fixture.Current);
            Ui.Texts[levelPath] = (++fixture.Level).ToString();
        };
        Ui.Numbers[orbs] = balance;
        return fixture;
    }

    private static void HolyUpgradeStrategies()
    {
        var fixture = SetupHoly(50);
        Run(HolyUpgradeBuyer.Run("Vermilion,Grace", UncappedUpgradeMode.Focus, () => fixture.Retry = true));
        Check(fixture.Selected.SequenceEqual(new[] { "Vermilion" }) && fixture.Bought.Count == 0,
            "Focus did not save for the first unaffordable guardian.");

        fixture = SetupHoly(50);
        Run(HolyUpgradeBuyer.Run("Vermilion,Grace", UncappedUpgradeMode.Balanced, () => fixture.Retry = true));
        Check(fixture.Selected.SequenceEqual(new[] { "Vermilion", "Grace" }) &&
              fixture.Bought.Count == 5 && fixture.Bought.All(name => name == "Grace"),
            "Balance did not visit guardians in priority order and spend on an affordable choice.");

        fixture = SetupHoly(200);
        fixture.Costs["Vermilion"] = 10;
        Run(HolyUpgradeBuyer.Run("Vermilion,Grace", UncappedUpgradeMode.Focus, () => fixture.Retry = true));
        Check(fixture.Bought.Count == 10 && fixture.Bought.All(name => name == "Vermilion") && fixture.Retry,
            "A capped focused pass did not retry the same guardian.");

        fixture = SetupHoly(20);
        fixture.Guardians["Vermilion"].IsUnlocked.Value = false;
        Run(HolyUpgradeBuyer.Run("Vermilion,Grace", UncappedUpgradeMode.Focus, () => fixture.Retry = true));
        Check(fixture.Selected.SequenceEqual(new[] { "Grace" }) && fixture.Bought.Count == 2,
            "A locked guardian blocked the first unlocked priority.");

        fixture = SetupHoly(200);
        fixture.NoProgress = true;
        Run(HolyUpgradeBuyer.Run("Vermilion,Grace", UncappedUpgradeMode.Balanced, () => fixture.Retry = true));
        Check(fixture.Selected.SequenceEqual(new[] { "Vermilion" }) && fixture.Bought.Count == 0,
            "An unconfirmed purchase fell through to a lower guardian.");

        fixture = SetupHoly(200);
        fixture.WrongPreview = true;
        Run(HolyUpgradeBuyer.Run("Vermilion,Grace", UncappedUpgradeMode.Balanced, () => fixture.Retry = true));
        Check(fixture.Selected.SequenceEqual(new[] { "Vermilion" }) && fixture.Bought.Count == 0,
            "An unverified guardian preview was used.");
    }

    private sealed class BubbleTask : BotTask
    {
        protected override string NotificationPath => "bubble";
        protected override bool EnabledByDefault => true;
        public override IEnumerator Execute() { yield break; }
    }

    private sealed class PreciseTask : BotTask
    {
        protected override string NotificationPath => "bubble";
        protected override bool EnabledByDefault => true;
        protected override bool UseScheduledTime => true;
        public override IEnumerator Execute() { yield break; }
    }

    private static void Scheduling()
    {
        var past = DateTime.Now.AddHours(-1);
        var bubble = new BubbleTask();
        bubble.InitializeConfig("test");
        bubble.RestoreNextRun(past);
        var precise = new PreciseTask();
        precise.InitializeConfig("test");
        precise.RestoreNextRun(DateTime.Now.AddMinutes(20));
        Check(bubble.DueTime == past.AddHours(6), "Bubble fallback time was wrong.");
        Check(!bubble.IsReady(false), "Bubble became ready before its true due time.");
        Check(precise.DueTime == precise.NextRunTime, "Precise task received fallback delay.");
        ScheduleStore.Save(new BotTask[] { bubble, precise });

        var restoredBubble = new BubbleTask();
        var restoredPrecise = new PreciseTask();
        ScheduleStore.Restore(new BotTask[] { restoredBubble, restoredPrecise });
        Check(restoredBubble.NextRunTime == past, "Future bubble fallback was not restored.");
        Check(restoredPrecise.NextRunTime == precise.NextRunTime, "Precise due time was not restored.");
        bubble.RestoreNextRun(DateTime.Now.AddHours(-7));
        precise.RestoreNextRun(DateTime.Now.AddMinutes(-1));
        ScheduleStore.Save(new BotTask[] { bubble, precise });
        Check(File.ReadAllLines(Path.Combine("UserData", "ZdbrusBotSchedule.txt")).Length == 0, "Expired schedule was persisted.");
        bubble.RestoreNextRun(DateTime.MinValue);
        Check(bubble.DueTime == DateTime.MinValue, "Unscheduled task received a delay.");
        bubble.RestoreNextRun(DateTime.MaxValue.AddHours(-1));
        Check(bubble.DueTime == DateTime.MaxValue, "Fallback overflowed.");
    }

    private static void RememberedNames()
    {
        GameObject.Clear();
        var oracle = GameObject.CreatePath("menus/Oracle");
        GameObject.CreatePath("menus/Town");
        var blessing = GameObject.CreatePath("menus/Oracle/submenus/bg/blessingsSubmenu/blessings/blessing (0)").transform;
        const string legacySuffix = "|blessing (0)#0/blessings#0/blessingsSubmenu#0/bg#0/submenus#0/Oracle#";
        File.WriteAllText(Path.Combine("UserData", "ZdbrusBotUpgradeNames.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["oracle-blessings" + legacySuffix + "5"] = "Raining gold",
                ["oracle-blessings" + legacySuffix + "4"] = "Raining gold",
                ["oracle-conflict" + legacySuffix + "5"] = "Raining gold",
                ["oracle-conflict" + legacySuffix + "4"] = "Fate"
            }));
        Check(UpgradeNames.TryGet(blessing, "oracle-blessings", out var migrated) && migrated == "Raining gold",
            "Legacy Oracle names were not reused after menu reordering.");
        Check(!UpgradeNames.TryGet(blessing, "oracle-conflict", out _), "Conflicting legacy names were trusted.");
        var key = UpgradeNames.KeyOf(blessing, "oracle-blessings");
        oracle.transform.SetAsLastSibling();
        Check(UpgradeNames.KeyOf(blessing, "oracle-blessings") == key &&
              UpgradeNames.TryGet(blessing, "oracle-blessings", out migrated) && migrated == "Raining gold",
            "Opening another menu invalidated the remembered Oracle names.");
        var node = GameObject.CreatePath("root/tree/node").transform;
        UpgradeNames.Remember(node, "ledra", "Raining gold");
        UpgradeNames.Remember(node, "yamanoth", "Firestone finder");
        UpgradeNames.Flush();
        Check(UpgradeNames.TryGet(node, "ledra", out var first) && first == "Raining gold", "First scope lost.");
        Check(UpgradeNames.TryGet(node, "yamanoth", out var second) && second == "Firestone finder", "Second scope collided.");
        Check(UpgradeNames.Matches(node, "ledra", first, " RAINING GOLD "), "Matching preview was rejected.");
        Check(!UpgradeNames.Matches(node, "ledra", first, ""), "Unreadable preview was accepted.");
        Check(UpgradeNames.TryGet(node, "ledra", out first) && first == "Raining gold", "Unreadable preview replaced the name.");
        Check(!UpgradeNames.Matches(node, "ledra", first, "Guardian power"), "Changed preview was accepted.");
        Check(UpgradeNames.TryGet(node, "ledra", out first) && first == "Guardian power", "Changed preview was not remembered.");
        UpgradeNames.Flush();
        var json = File.ReadAllText(Path.Combine("UserData", "ZdbrusBotUpgradeNames.json"));
        Check(json.Contains("Guardian power") && !json.Contains("cost", StringComparison.OrdinalIgnoreCase), "Cache includes unexpected data.");
    }

    private static void PointerHold()
    {
        GameObject.Clear();
        var gameObject = GameObject.CreatePath("battle/upgrade");
        var button = gameObject.AddComponent<Button>();
        EventSystem.current = new EventSystem();
        ExecuteEvents.Events.Clear();
        Time.unscaledTime = 0;
        var cached = new CachedGameButton("battle/upgrade");
        var hold = cached.HoldButton(1f);
        Check(hold.MoveNext(), "Hold did not start.");
        Time.unscaledTime = 2;
        Check(!hold.MoveNext(), "Hold did not end.");
        Check(ExecuteEvents.Events.SequenceEqual(new[] { "down", "up", "click" }), "Pointer sequence was not balanced.");

        ExecuteEvents.Events.Clear();
        Time.unscaledTime = 0;
        hold = cached.HoldButton(1f);
        Check(hold.MoveNext(), "Second hold did not start.");
        cached.CancelHold();
        ((IDisposable)hold).Dispose();
        Check(ExecuteEvents.Events.SequenceEqual(new[] { "down", "up" }), "Cancellation did not release exactly once.");

        ExecuteEvents.Events.Clear();
        Time.unscaledTime = 0;
        hold = cached.HoldButton(1f);
        Check(hold.MoveNext(), "Third hold did not start.");
        button.interactable = false;
        Check(!hold.MoveNext(), "Disabled button did not end the hold.");
        Check(ExecuteEvents.Events.SequenceEqual(new[] { "down", "up" }), "Disabled button received a click.");
    }

    private static void ResetMap()
    {
        GameObject.Clear();
        Logger.Messages.Clear();
        MapFixture.Missions.Clear();
        MapFixture.Selected = null;
        MapFixture.Starts = 0;
        MapFixture.StartMode = "success";
        MapFixture.Free = -1;
        Ui.OpenMapCalls = 0;
        Ui.OpenMapWorks = true;
        MapMission.NextRunTime = DateTime.MinValue;
    }

    private static void AddMission(string name, DateTime ends)
    {
        GameObject.CreatePath("mapPins/region/" + name);
        MapFixture.Missions[name] = new MapFixture.Mission { Ends = ends };
    }

    private static void MapFailures()
    {
        ResetMap();
        Ui.OpenMapWorks = false;
        var task = new MapMissionsTask();
        Run(task.Execute());
        Check(Ui.OpenMapCalls == 1 && task.NextRunTime > DateTime.Now.AddMinutes(14), "Missing map did not retry.");

        ResetMap();
        task = new MapMissionsTask();
        Run(task.Execute());
        Check(Ui.OpenMapCalls == 1 && task.NextRunTime > DateTime.Now.AddMinutes(14), "Unreadable counter did not retry.");
        Check(Logger.Messages.Any(x => x.Contains("Could not read the free-squad counter")), "Unreadable counter was not reported.");

        ResetMap();
        MapFixture.Free = 1;
        MapFixture.StartMode = "unreadable";
        AddMission("first", DateTime.Now.AddHours(2));
        AddMission("second", DateTime.Now.AddHours(1));
        task = new MapMissionsTask();
        Run(task.Execute());
        Check(MapFixture.Starts == 1 && !MapFixture.Missions["first"].Active, "Unreadable counter falsely confirmed a mission.");
        Check(task.NextRunTime > DateTime.Now.AddMinutes(14) && task.NextRunTime < DateTime.Now.AddMinutes(16), "Failed start retry was wrong.");
    }

    private static void MapStart()
    {
        ResetMap();
        MapFixture.Free = 1;
        var end = DateTime.Now.AddHours(1);
        AddMission("first", end);
        var task = new MapMissionsTask();
        Run(task.Execute());
        Check(MapFixture.Starts == 1 && MapFixture.Missions["first"].Active, "Mission was not started.");
        Check(Math.Abs((task.NextRunTime - end.AddSeconds(-BotSettings.FreeSpeedupSeconds)).TotalSeconds) < 1,
            "Mission timer did not use the configured free-speedup threshold.");
    }

    private sealed class ResearchFixture
    {
        public readonly List<int> Selected = new();
        public readonly List<GameObject> Slots = new();
        public GameObject Preview;
        public Button Close;
        public Button Start;
        public int CurrentNode;
        public int Starts;
    }

    private static ResearchFixture SetupResearch(int slots = 1, int firstId = 0)
    {
        GameObject.Clear();
        var fixture = new ResearchFixture();
        GameObject.CreatePath("menus/Library/submenus/firestoneResearch");
        var nodeRoot = Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.NodeLoc.Root;
        var tree = nodeRoot + "/tree1";
        for (var i = 0; i < 16; i++)
        {
            var id = firstId + i;
            var path = tree + "/firestoneResearch" + id;
            var node = GameObject.CreatePath(path);
            GameObject.CreatePath(path + "/glow").activeSelf = false;
            GameObject.CreatePath(path + "/genericText").activeSelf = false;
            GameObject.CreatePath(path + "/progressBarBg");
            node.AddComponent<Button>().onClick.Action = () =>
            {
                fixture.Selected.Add(id);
                fixture.CurrentNode = id;
                fixture.Preview.activeSelf = true;
            };
        }

        var panel = Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.Root;
        var table = GameObject.CreatePath(panel + "/selectResearchTable");
        for (var i = 0; i < slots; i++)
        {
            var slot = GameObject.CreatePath(panel + "/researchSlot" + i + "/container/researchInfo");
            slot.activeSelf = false;
            fixture.Slots.Add(slot);
        }

        var previewRoot = Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.Root;
        fixture.Preview = GameObject.CreatePath(previewRoot);
        GameObject.CreatePath(previewRoot + "/bg/unlocked");
        GameObject.CreatePath(previewRoot + "/bg/maxed").activeSelf = false;
        fixture.Close = GameObject.CreatePath(previewRoot + "/bg/closeButton").AddComponent<Button>();
        fixture.Close.onClick.Action = () => fixture.Preview.activeSelf = false;
        fixture.Start = GameObject.CreatePath(
            Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.ActivateBtn).AddComponent<Button>();
        fixture.Start.onClick.Action = () =>
        {
            fixture.Starts++;
            var empty = fixture.Slots.FirstOrDefault(slot => !slot.activeSelf);
            if (empty != null) empty.activeSelf = true;
            GameObject.Resolve(tree + "/firestoneResearch" + fixture.CurrentNode + "/glow").activeSelf = true;
            if (fixture.Slots.All(slot => slot.activeSelf)) table.activeSelf = false;
        };
        fixture.Preview.activeSelf = false;
        return fixture;
    }

    private static FirestoneResearchTask ResearchTask(string priority)
    {
        var task = new FirestoneResearchTask();
        task.InitializeConfig("test");
        ((MelonPreferences_Entry<string>)MelonPreferences.Categories["firestone_research"].Entries["research_priority"]).Value = priority;
        return task;
    }

    private static void ResearchIds()
    {
        var fixture = SetupResearch(firstId: 16);
        var result = ResearchSelectionResult.Failed;
        Run(new Node().Select(13, value => result = value));
        Check(result == ResearchSelectionResult.Selected && fixture.Selected.SequenceEqual(new[] { 28 }),
            "One-based ID did not select the thirteenth named node.");
    }

    private static void ResearchMultipleSlots()
    {
        var fixture = SetupResearch(slots: 2);
        Run(ResearchTask("13,7,1").Execute());
        Check(fixture.Starts == 2, "Both empty research slots were not filled.");
        Check(fixture.Selected.SequenceEqual(new[] { 12, 6 }), "Research priority was not respected.");
    }

    private static void ResearchPreviewFailures()
    {
        var fixture = SetupResearch();
        fixture.Preview.activeSelf = true;
        fixture.Close.interactable = false;
        Run(ResearchTask("13,7").Execute());
        Check(fixture.Starts == 0 && fixture.Selected.Count == 0, "Stale preview was used.");

        fixture = SetupResearch();
        var tree = Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.NodeLoc.Root + "/tree1/firestoneResearch12";
        GameObject.Resolve(tree).TryGetComponent(out Button first);
        first.onClick.Action = () => fixture.Selected.Add(12);
        Run(ResearchTask("13,7").Execute());
        Check(fixture.Starts == 0 && fixture.Selected.SequenceEqual(new[] { 12 }), "Failed high-priority preview fell through.");
    }

    private static void ResearchBlockedFallback()
    {
        var fixture = SetupResearch(slots: 2);
        fixture.Slots[0].activeSelf = true;
        fixture.Start.interactable = false;
        var panel = Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.Root;
        GameText.Times[panel + "/researchSlot0" +
            Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.NextRunTimeTxt] = DateTime.Now.AddHours(1);
        var task = ResearchTask("");
        Run(task.Execute());
        Check(task.NextRunTime <= DateTime.Now.AddMinutes(15.1),
            "Blocked fallback did not schedule an early retry.");
        Check(fixture.Starts == 0, "A disabled research control started a slot.");
    }
}
