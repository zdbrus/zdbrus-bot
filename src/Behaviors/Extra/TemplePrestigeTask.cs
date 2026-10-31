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
///     Prestige ("Empower now") once the run has stalled: no new highest stage for a few hours, and the temple offers at
///     least a minimum gold boost. Never uses the paid 2x empower.
/// </summary>
public class TemplePrestigeTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/TemplePrestige";
    protected override bool UseScheduledTime => true;
    internal override int SchedulerPriority => 1;

    private const string T = P.Menus + "/TempleOfEternals";
    private const string Info = T + "/submenus/bgNew/prestigeSubmenu/adventureInfo";
    private const string Stage = P.Battle + "/topSideUI/stageProgression/currentStage/stageNum";
    private const string EmpowerPopup = P.Popups + "/EmpowerPopup";
    private const string FreeEmpower = EmpowerPopup + "/bg/empowerBg/empowerButton";
    private const string FreeGain = EmpowerPopup + "/bg/empowerBg/empowerRainingGold/value";
    private const string PrestigeComplete = P.Popups + "/TOEPrestigeComplete";
    private MelonPreferences_Entry<int> _stallHours;
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
        _stallHours = c.CreateEntry("stall_hours", 3, "Hours Without Progress",
            "Empower once no new highest stage has been reached for this many hours (the run has stalled).");
        _minGain = c.CreateEntry("min_gain_percent", 100, "Minimum Gold Boost %",
            "Never empower for less than this extra gold (the +% the temple shows).");
        _minHours = c.CreateEntry("min_hours_played", 1, "Minimum Hours In Run", "Never empower a run younger than this.");
        _efficiency = c.CreateEntry("efficiency_percent", 75, "Growth Rate Threshold %",
            "After two hourly samples below this percentage of the run's average boost growth, consider empowering. " +
            "This is an estimate, not a proven optimum. 0 disables this trigger; the stall trigger remains active.");
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
        stage = 0;
        stalled = 0;
        var stageValue = Ui.NumberAt(Stage);
        if (stageValue <= 0 || stageValue > int.MaxValue || Math.Truncate(stageValue) != stageValue) return false;
        stage = (int)stageValue;
        var now = DateTime.Now;
        DateTime.TryParse(_bestAt.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at);
        var newRun = _bestStage.Value > 0 && stage < _bestStage.Value;
        if (newRun) ResetSamples();
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

    private static IEnumerator CloseTemple()
    {
        if (Ui.Visible(EmpowerPopup)) yield return Ui.Click(EmpowerPopup + "/bg/closeButton", 0.5f);
        if (Ui.Visible(T)) yield return Ui.Click(T + "/closeButton", 0.5f);
    }

    public override IEnumerator Execute()
    {
        BubbleQuietUntil = DateTime.Now.AddMinutes(15);
        NextRunTime = DateTime.Now.AddMinutes(30);
        if (_stallHours.Value < 1 || _minGain.Value < 0 || _minHours.Value < 0 || _efficiency.Value < 0)
        {
            InvalidateRate();
            Report("Invalid prestige settings; no reset attempted.");
            Logger.Warning("Temple: a threshold is negative or the stall time is below one hour; check prestige settings.");
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

        var isStalled = stalled >= _stallHours.Value;
        var hasSampleTime = DateTime.TryParse(_lastGainAt.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var sampledAt);
        var sampleDue = _efficiency.Value > 0 && (!hasSampleTime || sampledAt > DateTime.Now ||
                        DateTime.Now - sampledAt >= TimeSpan.FromHours(1));
        var stageReading = $"Stage {stage} | {stalled:0.0}h without a new best";
        if (!isStalled && !sampleDue)
        {
            Report("Waiting for progress or the next growth sample.", stageReading);
            Logger.Debug($"Temple: {stageReading}; stall trigger is {_stallHours.Value}h.");
            yield break;
        }

        if (Ui.Visible(EmpowerPopup))
        {
            InvalidateRate();
            Report("An empower preview is already open; leaving it untouched.", stageReading);
            Logger.Warning("Temple: an existing empower preview was left untouched.");
            yield break;
        }

        yield return Ui.OpenTownBuilding("templeOfEternals");
        yield return Ui.WaitFor(T);
        if (!Ui.Visible(T) || !Ui.Visible(Info))
        {
            InvalidateRate();
            NextRunTime = DateTime.Now.AddMinutes(5);
            Report("Temple screen unavailable; no reset attempted.", stageReading);
            Logger.Warning("Temple: the prestige screen did not open; retrying in five minutes.");
            yield return CloseTemple();
            yield break;
        }

        var playedText = Ui.Text(Info + "/adventureTimePlayed");
        if (!PrestigePolicy.TryParseRunHours(playedText, out var played))
        {
            InvalidateRate();
            Report("Run age unreadable; no reset attempted.", stageReading);
            Logger.Warning($"Temple: run age '{playedText}' is unreadable; skipped.");
            yield return CloseTemple();
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
            stageReading = $"Stage {stage} | 0.0h without a new best";
            Logger.Info("Temple: new or previously untracked run; cleared old growth samples.");
        }

        _lastRunHours.Value = played;
        _lastRunAt.Value = observedAt.ToString("o");
        MelonPreferences.Save();
        var opened = false;
        yield return Ui.ClickUntil(Ui.Find(Info + "/openEmpowerButton"), () => Ui.Visible(EmpowerPopup),
            value => opened = value, "open free prestige preview", 1f, 4f);
        if (!opened)
        {
            InvalidateRate();
            Report("Prestige preview unavailable; no reset attempted.", stageReading);
            yield return CloseTemple();
            yield break;
        }

        var gainText = Ui.Text(FreeGain);
        if (!PrestigePolicy.TryParseGain(gainText, out var gain))
        {
            InvalidateRate();
            Report("Free gold boost unreadable; no reset attempted.", stageReading + $" | run {played:0.0}h");
            Logger.Warning($"Temple: free preview boost '{gainText}' is unreadable or unsupported; skipped.");
            yield return CloseTemple();
            yield break;
        }

        var reading = $"{stageReading} | run {played:0.0}h | boost {gainText}";
        Logger.Info($"Temple: {reading}.");
        if (!isStalled)
        {
            if (_efficiency.Value == 0)
            {
                Report("Waiting for the stall threshold.", reading);
                yield return CloseTemple();
                yield break;
            }

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

            if (rate.State != PrestigeRateState.Trigger)
            {
                var reason = rate.State switch
                {
                    PrestigeRateState.Invalid => "Growth rate unavailable; keeping the run.",
                    PrestigeRateState.Baseline => "First growth sample for this run.",
                    PrestigeRateState.TooSoon => "Waiting for the next hourly growth sample.",
                    PrestigeRateState.Slow => "Growth below threshold once; one more sample needed.",
                    _ => "Growth remains above the threshold."
                };
                Report(reason, reading);
                Logger.Info($"Temple: {reason} Rate {rate.Rate:0.##}%/h; limit {rate.Limit:0.##}%/h.");
                yield return CloseTemple();
                yield break;
            }

            Logger.Info($"Temple: two slow samples ({rate.Rate:0.##}%/h vs {rate.Limit:0.##}%/h); checking minimums.");
        }

        if (gain < _minGain.Value || played < _minHours.Value)
        {
            Report(gain < _minGain.Value ? "Waiting for the minimum gold boost." : "Waiting for the minimum run age.", reading);
            yield return CloseTemple();
            yield break;
        }

        var currentStage = Ui.NumberAt(Stage);
        var latestGainText = Ui.Text(FreeGain);
        var latestAgeText = Ui.Text(Info + "/adventureTimePlayed");
        if (currentStage <= 0 || currentStage < stage || (isStalled && currentStage > _bestStage.Value) ||
            !PrestigePolicy.TryParseGain(latestGainText, out var latestGain) || latestGain < gain ||
            !PrestigePolicy.TryParseRunHours(latestAgeText, out var latestAge) ||
            PrestigePolicy.IsNewRun(played, latestAge) || latestAge < _minHours.Value)
        {
            InvalidateRate();
            Report("Run changed during the check; waiting for a fresh reading.", reading);
            Logger.Warning("Temple: run state or preview changed during the decision; skipped.");
            yield return CloseTemple();
            yield break;
        }

        var freeButton = Ui.Find(FreeEmpower);
        if (!Ui.Clickable(freeButton) || HasPriceMarker(freeButton))
        {
            InvalidateRate();
            Report("Free empower control unavailable; no reset attempted.", reading);
            Logger.Warning("Temple: the verified free empower button was unavailable or showed a price; skipped.");
            yield return CloseTemple();
            yield break;
        }

        var confirmed = false;
        yield return Ui.ClickUntil(freeButton, () =>
        {
            var currentStage = Ui.NumberAt(Stage);
            return Ui.Visible(PrestigeComplete) || (currentStage > 0 && currentStage < stage);
        }, value => confirmed = value, "free temple empower", 2f, 8f);
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
        _bestStage.Value = 0;
        _bestAt.Value = "";
        MelonPreferences.Save();
        Report(startConfirmed ? "Prestige confirmed; tracking the new run."
            : "Prestige completed; start screen needs attention.", reading);
    }
}
