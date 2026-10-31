using System;
using System.Collections;
using System.Globalization;
using Firebot.Core;
using Firebot.Core.Tasks;
using MelonLoader;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

/// <summary>
///     Uses the free empower when growth efficiency drops, boss retries repeat at one stage, or the fallback stall timer
///     expires. Minimum run age and gold boost still guard every trigger.
/// </summary>
public class TemplePrestigeTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/TemplePrestige";
    protected override bool UseScheduledTime => true;
    internal override int SchedulerPriority => 1;

    private const string T = P.Menus + "/TempleOfEternals";
    private const string Info = T + "/submenus/bgNew/prestigeSubmenu/adventureInfo";
    private const string EmpowerPopup = P.Popups + "/EmpowerPopup";
    private const string FreeEmpower = EmpowerPopup + "/bg/empowerBg/empowerButton";
    private const string FreeGain = EmpowerPopup + "/bg/empowerBg/empowerRainingGold/value";
    private const string PrestigeComplete = P.Popups + "/TOEPrestigeComplete";
    private const string Confirmation = P.Popups + "/ActionRequired";
    private const string ConfirmationMessage = Confirmation + "/bg/messageText";
    private const string ConfirmEmpower = Confirmation + "/bg/confirmButton";
    private const string EmpowerQuestion = "Are you sure that you want to empower the temple?";
    private MelonPreferences_Entry<int> _stallMinutes;
    private MelonPreferences_Entry<int> _bossAttempts;
    private MelonPreferences_Entry<int> _minGain;
    private MelonPreferences_Entry<int> _minHours;
    private MelonPreferences_Entry<int> _bestStage;
    private MelonPreferences_Entry<string> _bestAt;
    private MelonPreferences_Entry<int> _efficiency;
    private MelonPreferences_Entry<double> _lastGain;
    private MelonPreferences_Entry<string> _lastGainAt;
    private MelonPreferences_Entry<double> _lastRunHours;
    private MelonPreferences_Entry<string> _lastRunAt;
    private int _slowStreak;

    internal static string LastDecision { get; private set; } = "No prestige check yet.";
    internal static string LastReading { get; private set; } = "Waiting for the bot to check this run.";
    internal static DateTime? LastCheckedAt { get; private set; }

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        c.CreateEntry("stall_hours", 1, "Hours Without Progress (Legacy)",
            "Replaced by the 30-minute or 1-hour fallback setting.", is_hidden: true);
        _stallMinutes = c.CreateEntry("stall_minutes", 30, "Fallback Stall Timer",
            "Fallback reset delay after no new highest stage.");
        _bossAttempts = c.CreateEntry("boss_attempts", 3, "Boss Attempts Before Reset",
            "Reset after this many boss attempts at the same stage.");
        _minGain = c.CreateEntry("min_gain_percent", 100, "Minimum Gold Boost %",
            "Never empower for less than this extra gold (the +% the temple shows).");
        _minHours = c.CreateEntry("min_hours_played", 1, "Minimum Hours In Run", "Never empower a run younger than this.");
        _efficiency = c.CreateEntry("efficiency_percent", 75, "Growth Rate Threshold %",
            "After two hourly samples below this percentage of the run's average boost growth, empower without waiting for the fallbacks. " +
            "This is an estimate, not a proven optimum. 0 disables this trigger.");
        _lastGain = c.CreateEntry("last_gain", -1.0, "Last Gold Boost Sample", "Kept by the bot.", is_hidden: true);
        _lastGainAt = c.CreateEntry("last_gain_at", "", "Last Sample Time", "Kept by the bot.", is_hidden: true);
        _lastRunHours = c.CreateEntry("last_observed_run_hours", -1.0, "Last Run Age", "Kept by the bot.", is_hidden: true);
        _lastRunAt = c.CreateEntry("last_observed_run_at", "", "Last Run Age Time", "Kept by the bot.", is_hidden: true);
        c.CreateEntry("reference_rate", 0.0, "Previous Run Gain Rate", "Kept for compatibility.", is_hidden: true);
        _bestStage = c.CreateEntry("best_stage", 0, "Best Stage", "Highest stage seen this run (kept by the bot).", is_hidden: true);
        _bestAt = c.CreateEntry("best_stage_at", "", "Best Stage Time", "When the highest stage was reached (kept by the bot).", is_hidden: true);
    }

    private static void Report(string decision, string reading = null)
    {
        LastDecision = decision;
        if (reading != null) LastReading = reading;
        LastCheckedAt = DateTime.Now;
    }

    private void ResetSamples()
    {
        _lastGain.Value = -1;
        _lastGainAt.Value = "";
        _lastRunHours.Value = -1;
        _lastRunAt.Value = "";
        _slowStreak = 0;
    }

    private void InvalidateRate()
    {
        _lastGain.Value = -1;
        _lastGainAt.Value = "";
        _slowStreak = 0;
        MelonPreferences.Save();
    }

    private bool TryGetStall(out int stage, out double stalled)
    {
        stalled = 0;
        if (!PlayerStats.TryGetCurrentStage(out stage)) return false;
        var now = DateTime.Now;
        DateTime.TryParse(_bestAt.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at);
        var newRun = _bestStage.Value > 0 && stage < _bestStage.Value;
        if (newRun)
        {
            ResetSamples();
            BossAttemptTracker.Reset();
        }
        if (stage > _bestStage.Value || newRun || at == default || at > now)
        {
            _bestStage.Value = stage;
            _bestAt.Value = now.ToString("o");
            MelonPreferences.Save();
            return true;
        }

        stalled = (now - at).TotalHours;
        return true;
    }

    private static bool HasPriceMarker(Transform button)
    {
        if (button == null) return true;
        foreach (var child in button.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || !child.gameObject.activeInHierarchy || child == button) continue;
            var name = child.name.ToLowerInvariant();
            if (name.Contains("cost") || name.Contains("price") || name.Contains("currency")) return true;
        }

        return false;
    }

    private static IEnumerator CloseTemple(bool closeScreen)
    {
        if (!closeScreen) yield break;
        if (Ui.Visible(EmpowerPopup)) yield return Ui.Click(EmpowerPopup + "/bg/closeButton", 0.5f);
        if (Ui.Visible(T)) yield return Ui.Click(T + "/closeButton", 0.5f);
    }

    private bool StageStillQualifies(int stage, PrestigeTrigger trigger)
        => PlayerStats.TryGetCurrentStage(out var current) && current >= stage &&
           (trigger == PrestigeTrigger.Efficiency || current <= _bestStage.Value);

    private static bool PrestigeConfirmed(int stage)
        => Ui.Visible(PrestigeComplete) ||
           (PlayerStats.TryGetCurrentStage(out var current) && current < stage);

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(15);
        NextRunTime = DateTime.Now.AddMinutes(1);
        if (Ui.Visible(Confirmation))
        {
            Report("An existing confirmation needs attention; left untouched.");
            Logger.Warning("Temple: a confirmation was already open; no prestige action attempted.");
            yield break;
        }
        if (_stallMinutes.Value is < 30 or > 60 || _bossAttempts.Value is < 2 or > 5 ||
            _minGain.Value < 0 || _minHours.Value < 0 || _efficiency.Value < 0)
        {
            InvalidateRate();
            Report("Invalid prestige settings; no reset attempted.");
            Logger.Warning("Temple: a prestige threshold is outside its supported range; check the settings.");
            yield break;
        }
        if (_efficiency.Value == 0 && (_lastGain.Value >= 0 || _slowStreak != 0))
            InvalidateRate();

        if (!TryGetStall(out var stage, out var stalled))
        {
            InvalidateRate();
            NextRunTime = DateTime.Now.AddMinutes(5);
            Report("Stage unreadable; no reset attempted.", "The current stage could not be read.");
            Logger.Warning("Temple: current stage is unreadable; retrying in five minutes.");
            yield break;
        }

        var isStalled = stalled >= _stallMinutes.Value / 60d;
        var bossAttempts = BossAttemptTracker.CountAtStage(stage);
        var bossRetriesReached = bossAttempts >= _bossAttempts.Value;
        var hasSampleTime = DateTime.TryParse(_lastGainAt.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var sampledAt);
        var sampleDue = _efficiency.Value > 0 && (!hasSampleTime || sampledAt > DateTime.Now ||
                        DateTime.Now - sampledAt >= TimeSpan.FromHours(1));
        var stageReading = $"Stage {stage} | {stalled:0.0}h without a new best | boss tries {bossAttempts}/{_bossAttempts.Value}";
        if (!isStalled && !sampleDue && !bossRetriesReached)
        {
            Report("Waiting for progress or the next growth sample.", stageReading);
            Logger.Debug($"Temple: {stageReading}; fallback timer is {_stallMinutes.Value} minutes.");
            yield break;
        }

        var previewAlreadyOpen = Ui.Visible(EmpowerPopup);
        var templeWasVisible = Ui.Visible(T);
        var closeScreen = !previewAlreadyOpen && !templeWasVisible;
        if (!previewAlreadyOpen) yield return Ui.OpenTownBuilding("templeOfEternals");
        yield return Ui.WaitFor(T);
        if (!Ui.Visible(T) || !Ui.Visible(Info))
        {
            InvalidateRate();
            NextRunTime = DateTime.Now.AddMinutes(5);
            Report("Temple screen unavailable; no reset attempted.", stageReading);
            Logger.Warning("Temple: the prestige screen did not open; retrying in five minutes.");
            yield return CloseTemple(closeScreen);
            yield break;
        }

        var playedText = Ui.Text(Info + "/adventureTimePlayed");
        if (!PrestigePolicy.TryParseRunHours(playedText, out var played))
        {
            InvalidateRate();
            Report("Run age unreadable; no reset attempted.", stageReading);
            Logger.Warning($"Temple: run age '{playedText}' is unreadable; skipped.");
            yield return CloseTemple(closeScreen);
            yield break;
        }

        DateTime.TryParse(_lastRunAt.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var previousRunAt);
        var observedAt = DateTime.Now;
        if (PrestigePolicy.RunAgeDiscontinuous(_lastRunHours.Value, played, previousRunAt, observedAt))
        {
            ResetSamples();
            _bestStage.Value = stage;
            _bestAt.Value = DateTime.Now.ToString("o");
            stalled = 0;
            isStalled = false;
            bossRetriesReached = false;
            stageReading = $"Stage {stage} | 0.0h without a new best";
            Logger.Info("Temple: new or previously untracked run; cleared old growth samples.");
            BossAttemptTracker.Reset();
        }

        _lastRunHours.Value = played;
        _lastRunAt.Value = observedAt.ToString("o");
        MelonPreferences.Save();
        var opened = previewAlreadyOpen;
        if (!opened)
            yield return Ui.ClickUntil(Ui.Find(Info + "/openEmpowerButton"), () => Ui.Visible(EmpowerPopup),
                value => opened = value, "open free prestige preview", 1f, 4f);
        if (!opened)
        {
            InvalidateRate();
            Report("Prestige preview unavailable; no reset attempted.", stageReading);
            yield return CloseTemple(closeScreen);
            yield break;
        }

        var gainText = Ui.Text(FreeGain);
        if (!PrestigePolicy.TryParseGain(gainText, out var gain))
        {
            InvalidateRate();
            Report("Free gold boost unreadable; no reset attempted.", stageReading + $" | run {played:0.0}h");
            Logger.Warning($"Temple: free preview boost '{gainText}' is unreadable or unsupported; skipped.");
            yield return CloseTemple(closeScreen);
            yield break;
        }

        var reading = $"{stageReading} | run {played:0.0}h | boost {gainText}";
        Logger.Info($"Temple: {reading}.");
        var efficiencyTriggered = false;
        var efficiencyDecision = "Growth rate unavailable; keeping the run.";
        if (sampleDue)
        {
            DateTime.TryParse(_lastGainAt.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out sampledAt);
            var rate = PrestigePolicy.EvaluateRate(gain, played, _lastGain.Value, sampledAt, DateTime.Now,
                _slowStreak, _efficiency.Value);
            _slowStreak = rate.Streak;
            if (rate.State == PrestigeRateState.Invalid) InvalidateRate();
            if (rate.SaveSample)
            {
                _lastGain.Value = gain;
                _lastGainAt.Value = DateTime.Now.ToString("o");
                MelonPreferences.Save();
            }

            efficiencyTriggered = rate.State == PrestigeRateState.Trigger;
            efficiencyDecision = rate.State switch
            {
                PrestigeRateState.Invalid => "Growth rate unavailable; keeping the run.",
                PrestigeRateState.Baseline => "First growth sample for this run.",
                PrestigeRateState.TooSoon => "Waiting for the next hourly growth sample.",
                PrestigeRateState.Slow => "Growth below threshold once; one more sample needed.",
                PrestigeRateState.Trigger => "Growth efficiency threshold reached.",
                _ => "Growth remains above the threshold."
            };
            Logger.Info($"Temple: {efficiencyDecision} Rate {rate.Rate:0.##}%/h; limit {rate.Limit:0.##}%/h.");
        }

        var trigger = PrestigePolicy.SelectTrigger(efficiencyTriggered, bossRetriesReached, isStalled);
        if (trigger == PrestigeTrigger.None)
        {
            Report(efficiencyDecision, reading);
            yield return CloseTemple(closeScreen);
            yield break;
        }

        Logger.Info($"Temple: proceeding on {trigger} trigger.");
        if (gain < _minGain.Value || played < _minHours.Value)
        {
            Report(gain < _minGain.Value ? "Waiting for the minimum gold boost." : "Waiting for the minimum run age.", reading);
            yield return CloseTemple(closeScreen);
            yield break;
        }

        var stageKnown = PlayerStats.TryGetCurrentStage(out var currentStage);
        var latestGainText = Ui.Text(FreeGain);
        var latestAgeText = Ui.Text(Info + "/adventureTimePlayed");
        if (!stageKnown || currentStage < stage ||
            ((trigger != PrestigeTrigger.Efficiency) && currentStage > _bestStage.Value) ||
            !PrestigePolicy.TryParseGain(latestGainText, out var latestGain) || latestGain < gain ||
            !PrestigePolicy.TryParseRunHours(latestAgeText, out var latestAge) ||
            PrestigePolicy.IsNewRun(played, latestAge) || latestAge < _minHours.Value)
        {
            InvalidateRate();
            Report("Run changed during the check; waiting for a fresh reading.", reading);
            Logger.Warning($"Temple: run state or preview changed during the decision; skipped. " +
                $"Stage {stage} -> {(stageKnown ? currentStage.ToString(CultureInfo.InvariantCulture) : "unavailable")}; " +
                $"boost '{gainText}' -> '{latestGainText}'; run age '{playedText}' -> '{latestAgeText}'.");
            yield return CloseTemple(closeScreen);
            yield break;
        }

        var freeButton = Ui.Find(FreeEmpower);
        if (!Ui.Clickable(freeButton) || HasPriceMarker(freeButton))
        {
            InvalidateRate();
            Report("Free empower control unavailable; no reset attempted.", reading);
            Logger.Warning("Temple: the verified free empower button was unavailable or showed a price; skipped.");
            yield return CloseTemple(closeScreen);
            yield break;
        }

        var freeClicked = false;
        yield return Ui.ClickIf(FreeEmpower,
            () => !Ui.Visible(Confirmation) && !HasPriceMarker(Ui.Find(FreeEmpower)) &&
                  StageStillQualifies(stage, trigger),
            value => freeClicked = value, 2f, "free temple empower");
        var actionObserved = false;
        if (freeClicked)
            yield return Ui.WaitUntil(() => Ui.Visible(Confirmation) || PrestigeConfirmed(stage),
                value => actionObserved = value, 8f);

        var confirmed = actionObserved && PrestigeConfirmed(stage);
        if (freeClicked && Ui.Visible(Confirmation))
        {
            var confirmationClicked = false;
            yield return Ui.ClickIf(ConfirmEmpower,
                () => Ui.Visible(Confirmation) &&
                      string.Equals(Ui.Text(ConfirmationMessage), EmpowerQuestion, StringComparison.Ordinal) &&
                      !HasPriceMarker(Ui.Find(ConfirmEmpower)) && StageStillQualifies(stage, trigger),
                value => confirmationClicked = value, 2f, "confirm free temple empower");
            if (!confirmationClicked)
            {
                InvalidateRate();
                Report("Free prestige confirmation could not be verified; left untouched.", reading);
                Logger.Warning($"Temple: confirmation or live stage was not verified; no confirm clicked. " +
                    $"Message: '{Ui.Text(ConfirmationMessage)}'.");
                yield break;
            }
            confirmed = false;
            yield return Ui.WaitUntil(() => PrestigeConfirmed(stage), value => confirmed = value, 8f);
        }
        if (!confirmed)
        {
            InvalidateRate();
            Report("Empower was not confirmed; inspect the game.", reading);
            Logger.Warning("Temple: no completion screen or stage reset was observed; no other popup was clicked.");
            yield break;
        }

        var startConfirmed = true;
        if (Ui.Visible(PrestigeComplete))
        {
            startConfirmed = false;
            yield return Ui.ClickUntil(Ui.Find(PrestigeComplete + "/bg/confirmButton"),
                () => !Ui.Visible(PrestigeComplete), value => startConfirmed = value,
                "start after temple prestige", 1f, 5f);
            if (!startConfirmed) Logger.Warning("Temple: prestige completed, but the new-run start was not confirmed.");
        }

        ResetSamples();
        BossAttemptTracker.Reset();
        _bestStage.Value = 0;
        _bestAt.Value = "";
        MelonPreferences.Save();
        Report(startConfirmed ? "Prestige confirmed; tracking the new run."
            : "Prestige completed; start screen needs attention.", reading);
        Logger.Info($"Temple: {LastDecision}");
    }
}
