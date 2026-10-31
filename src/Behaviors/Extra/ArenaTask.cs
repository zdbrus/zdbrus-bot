using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Firebot.Core;
using Firebot.Core.Tasks;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Logger = Firebot.Core.Logger;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Firebot.Behaviors.Extra;

public class ArenaTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/ArenaTokens";

    private const string Menu = P.Menus + "/ArenaOfKings";
    private const string Arena = Menu + "/bg";
    private const string Tokens = Arena + "/battleTokens/quantity";
    private const string Grid = Arena + "/opponentHub/opponentGrid";
    private const string Refresh = Arena + "/opponentHub/refreshBg/refreshButton";
    private const string Preview = P.Popups + "/AOKBattlePreview";
    private const string PreviewFight = Preview + "/bg/mask/fightButton";
    private const string PreviewInfos = Preview + "/bg/mask/userInfos/";
    private const string Attacker = PreviewInfos + "arenaOfKingsAttackerInfoInteraction";
    private const string Defender = PreviewInfos + "arenaOfKingsDefenderInfoInteraction";
    private const string Result = P.Popups + "/AOKBattleResult";
    // The scheduler has a hard task deadline; reserve time for switching the campaign squad back.
    private const double RestoreAllowance = 45;
    private const double SwitchAllowance = 40;
    private const double FightSetupAllowance = 15;
    private readonly Func<DateTime> _now;
    private readonly Func<double> _seconds;
    private readonly Action<ArenaMatchupRecord> _recordMatchup;
    private MelonPreferences_Category _settings;
    private MelonPreferences_Entry<int> _margin;
    private MelonPreferences_Entry<bool> _dailyFight;
    private MelonPreferences_Entry<bool> _recordMatchups;
    private MelonPreferences_Entry<string> _lastForced;
    private MelonPreferences_Entry<int> _arenaSquad;
    private double _deadline;
    private bool _fought;

    public ArenaTask() : this(() => DateTime.Now, MonotonicSeconds) { }

    internal ArenaTask(Func<DateTime> now, Func<double> seconds, Action<ArenaMatchupRecord> recordMatchup = null)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
        _seconds = seconds ?? throw new ArgumentNullException(nameof(seconds));
        _recordMatchup = recordMatchup ?? ArenaMatchupRecorder.Enqueue;
    }

    private static double MonotonicSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private double Remaining => _deadline - _seconds();
    private bool HasTime(double reserve, double work) => Remaining >= reserve + work;
    private float WaitBudget(double reserve, double after, float maximum) =>
        (float)Math.Max(0, Math.Min(maximum, Remaining - reserve - after));

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _settings = c;
        _arenaSquad = c.CreateEntry("arena_squad", 0, "Arena Squad",
            "Squad (1, 2 or 3) to fight the arena with. The bot sets it active before arena fights and switches " +
            "back to your normal squad afterwards, so campaign keeps its own squad. 0 = don't switch.");
        _dailyFight = c.CreateEntry("one_fight_daily", true, "One Fight Per Day",
            "After 20:00, attempt the weakest opponent once if all are over the power limit and refresh is unavailable.");
        _lastForced = c.CreateEntry("last_forced_fight", "", "Last Forced Fight",
            "Internal: date of the last forced fight attempt.", true);
        _margin = c.CreateEntry("max_opponent_power_percent", 100, "Max Opponent Power %",
            "Normally fights opponents up to this % of your arena power (100 = equal or weaker).\n" +
            "The daily exception can exceed it. Power does not guarantee a win.");
        _recordMatchups = c.CreateEntry("record_matchups", false, "Record Arena Matchups",
            "Off by default. Records bot-started Arena outcomes locally; no player names.\n" +
            "Saved in UserData\\ZdbrusBotArenaMatchups.jsonl.\n" +
            "Does not predict winners or change fights.");
    }

    public override IEnumerator Execute()
    {
        _deadline = _seconds() + Math.Max(0, BotSettings.MaxTaskRuntime - 3);
        NextRunTime = _now().AddHours(3);
        if (Ui.Visible(Preview) || Ui.Visible(Result))
        {
            Retry("An Arena preview or result is already open; leaving it untouched.");
            yield break;
        }

        var want = _arenaSquad.Value;
        var before = Planner.SquadApplier.ActiveSlot();
        if (want < 0 || want > 3 || (want != 0 && (before < 1 || before > 3)))
        {
            Retry("The configured or active squad could not be verified; no fight was attempted.");
            yield break;
        }

        var swap = want != 0 && before != want;
        if (swap)
        {
            if (!Planner.SquadApplier.HasMachines(want))
            {
                Retry($"Arena squad {want} is empty or unavailable; no fight was attempted.");
                yield break;
            }

            yield return OpenArena();
            if (!Ui.Visible(Arena) || !ArenaPowerPolicy.TryParseTokens(Ui.Text(Tokens), out var tokens))
            {
                Retry("The Arena or battle-token count could not be read; squad left alone.");
                if (Ui.Visible(Menu)) yield return Ui.Click(Menu + "/closeButton");
                yield break;
            }

            yield return Ui.Click(Menu + "/closeButton");
            if (tokens == 0)
            {
                Logger.Debug("[Arena] No battle tokens; squad left alone.");
                yield break;
            }

            if (Planner.SquadApplier.ActiveSlot() != before)
            {
                Retry("The active squad changed before the Arena switch; no fight was attempted.");
                yield break;
            }
            if (!HasTime(RestoreAllowance, SwitchAllowance + FightSetupAllowance))
            {
                Retry("Max Task Runtime is too short to switch squads and safely attempt a fight.");
                yield break;
            }
            yield return Planner.SquadApplier.SetActive(want, "arena");
            if (Planner.SquadApplier.ActiveSlot() != want)
            {
                Retry($"Arena squad {want} did not become active; no fight was attempted.");
                yield return RestoreSquad(before);
                yield break;
            }
        }

        var reserve = swap ? RestoreAllowance : 5;
        for (var round = 0; round < 6; round++)
        {
            if (round > 0 && !HasTime(reserve, FightSetupAllowance))
            {
                Logger.Info("[Arena] Stopping this batch to leave time for squad restoration.");
                NextRunTime = _now().AddMinutes(1);
                break;
            }
            _fought = false;
            yield return Fight(want, reserve);
            if (!swap || !_fought) break;
        }

        if (swap) yield return RestoreSquad(before);
    }

    private static IEnumerator OpenArena()
    {
        yield return Ui.OpenTownBuilding("battles");
        yield return Ui.Click(P.Popups + "/WFMenuSelection/bg/arena", 2.5f);
    }

    private IEnumerator RestoreSquad(int before)
    {
        yield return Watchdog.ForceClearAll("after arena");
        if (Planner.SquadApplier.ActiveSlot() != before)
            yield return Planner.SquadApplier.SetActive(before, "back from arena");
        if (Planner.SquadApplier.ActiveSlot() != before)
            Logger.Warning($"[Arena] Could not verify that squad {before} was restored.");
    }

    private IEnumerator Fight(int expectedSquad, double reserve)
    {
        NextRunTime = _now().AddHours(3);
        if (!HasTime(reserve, FightSetupAllowance))
        {
            Retry("Not enough task runtime remains to start an Arena fight safely.");
            yield break;
        }
        if (Ui.Visible(Preview) || Ui.Visible(Result))
        {
            Retry("An Arena preview or result is already open; no fight was attempted.");
            yield break;
        }

        yield return OpenArena();
        if (!Ui.Visible(Arena) || !ArenaPowerPolicy.TryParseTokens(Ui.Text(Tokens), out var tokens))
        {
            Retry("The Arena or battle-token count could not be read.");
            yield break;
        }
        if (tokens == 0)
        {
            yield return Ui.Click(Menu + "/closeButton");
            yield break;
        }
        if (_margin.Value <= 0 ||
            !ArenaPowerPolicy.TryParseFormationPower(Ui.Text(Arena + "/battleFormation/totalPower"), out var myPower))
        {
            Retry("Arena squad power or the configured power limit is invalid; no fight was attempted.");
            yield return Ui.Click(Menu + "/closeButton");
            yield break;
        }

        var opponents = new List<Opponent>();
        var unreadable = 0;
        var clickable = 0;
        for (var wait = 0; wait < 20; wait++)
        {
            opponents = ReadOpponents(out unreadable, out clickable);
            if (opponents.Count > 0) break;
            yield return new WaitForSeconds(0.5f);
        }
        if (opponents.Count == 0)
        {
            Retry("No opponent had a readable name, rank and positive power.");
            yield return Ui.Click(Menu + "/closeButton");
            yield break;
        }

        Logger.Info($"[Arena] Arena squad power {myPower:N0}, active squad {Planner.SquadApplier.ActiveSlot()}.");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (!HasTime(reserve, 10))
            {
                Retry("Not enough task runtime remains to check another opponent.");
                yield return Ui.Click(Menu + "/closeButton");
                yield break;
            }
            var target = opponents.OrderBy(o => o.Power).ThenBy(o => o.Rank).First();
            var withinLimit = ArenaPowerPolicy.WithinLimit(myPower, target.Power, _margin.Value);
            var force = !withinLimit && unreadable == 0 && clickable >= 3 && CanForceToday();
            if (withinLimit || force)
            {
                yield return Challenge(target, force, expectedSquad, reserve);
                yield break;
            }

            if (!Ui.Clickable(Refresh))
            {
                if (unreadable > 0 || clickable < 3)
                    Logger.Warning("[Arena] Some opponents could not be verified; the daily exception was not used.");
                break;
            }

            var previous = Signature(opponents, unreadable, clickable);
            var refreshClicked = false;
            yield return Ui.ClickIf(Refresh, () => !Ui.Visible(Preview) && !Ui.Visible(Result),
                value => refreshClicked = value, 1f, "arena refresh");
            if (!refreshClicked)
            {
                Retry("The opponent refresh could not be clicked safely.");
                yield break;
            }
            var refreshed = false;
            string observed = null;
            for (var wait = 0; wait < 16; wait++)
            {
                if (!HasTime(reserve, 5)) break;
                var next = ReadOpponents(out var invalid, out var count);
                var signature = Signature(next, invalid, count);
                if (next.Count > 0 && signature != previous && signature == observed)
                {
                    opponents = next;
                    unreadable = invalid;
                    clickable = count;
                    refreshed = true;
                    break;
                }
                observed = next.Count > 0 && signature != previous ? signature : null;
                yield return new WaitForSeconds(0.5f);
            }
            if (!refreshed)
            {
                Retry("Opponent refresh did not produce a verifiable new list.");
                yield return Ui.Click(Menu + "/closeButton");
                yield break;
            }
        }

        Logger.Debug($"[Arena] No verified opponent within {_margin.Value}% of squad power; trying later.");
        NextRunTime = _now().AddMinutes(20);
        yield return Ui.Click(Menu + "/closeButton");
    }

    private IEnumerator Challenge(Opponent target, bool forced, int expectedSquad, double reserve)
    {
        if (Ui.Visible(Preview) || Ui.Visible(Result))
        {
            Retry("An Arena preview or result was already open; leaving it untouched.");
            yield break;
        }

        var selected = false;
        yield return Ui.ClickIf(target.Card.Find("fightButton"),
            () => !Ui.Visible(Preview) && !Ui.Visible(Result) && CardMatches(target),
            value => selected = value, 1.5f, "arena opponent preview");
        if (!selected)
        {
            Retry("The opponent or an open popup changed before the preview click.");
            yield break;
        }
        var ready = false;
        yield return Ui.WaitUntil(() => PreviewMatches(target, out _, out _) && Ui.Clickable(PreviewFight),
            value => ready = value, 4f);
        if (!ready)
        {
            Retry("The selected opponent's preview was missing, mismatched, unreadable, or not actionable.");
            yield return CloseOwnPreview();
            yield break;
        }

        var clicked = false;
        ArenaMatchupRecord matchup = null;
        yield return Ui.ClickIf(PreviewFight, () =>
        {
            try
            {
                if (_recordMatchups.Value && PreviewMatches(target, out var observedPower, out var observedOpponent))
                {
                    matchup = ArenaMatchupRecorder.Capture(Preview, _now().ToUniversalTime(), observedPower,
                        observedOpponent, _margin.Value, false);
                }
            }
            catch (Exception e)
            {
                Logger.Warning($"[Arena] Could not capture the matchup: {e.Message}");
            }
            if (!CanStartFight(target, forced, expectedSquad, reserve, out var myPower, out var theirPower,
                    out var usedDailyException))
                return false;
            if (matchup != null)
            {
                try
                {
                    if (matchup.PlayerPower != myPower || matchup.OpponentPower != theirPower ||
                        matchup.PowerLimitPercent != _margin.Value)
                    {
                        Logger.Warning("[Arena] Matchup values changed before the fight; no record will be saved.");
                        matchup = null;
                    }
                    else matchup.DailyException = usedDailyException;
                }
                catch (Exception e)
                {
                    Logger.Warning($"[Arena] Could not verify the matchup record: {e.Message}");
                    matchup = null;
                }
            }
            return true;
        }, value => clicked = value, 2f, "arena fight");
        if (!clicked)
        {
            Retry("The Arena fight click was not confirmed; the preview or squad may have changed.");
            yield return CloseOwnPreview();
            yield break;
        }

        var transitioned = false;
        yield return Ui.WaitUntil(() => !Ui.Visible(Preview) || Ui.Visible(Result), value => transitioned = value,
            WaitBudget(reserve, 9, 10f));
        if (!transitioned)
        {
            Retry("The fight click did not produce a confirmed transition.");
            yield return CloseOwnPreview();
            yield break;
        }

        var resultVisible = false;
        yield return Ui.WaitUntil(() => Ui.Visible(Result), value => resultVisible = value,
            WaitBudget(reserve, 9, 75f));
        if (!resultVisible)
        {
            Retry("The fight was clicked, but no Arena result was observed; no further fight was attempted.");
            yield break;
        }

        var resultReady = false;
        yield return Ui.WaitUntil(() => !string.IsNullOrWhiteSpace(ResultOpponent()) &&
            Ui.Visible(Result + "/bg/won") != Ui.Visible(Result + "/bg/lost"),
            value => resultReady = value, 4f);
        if (!resultReady || !string.Equals(ResultOpponent(), target.Name, StringComparison.Ordinal))
        {
            Retry("The Arena result was unreadable or belonged to a different opponent.");
            yield break;
        }

        var won = Ui.Visible(Result + "/bg/won");
        if (matchup != null)
        {
            try
            {
                if (_recordMatchups.Value)
                {
                    matchup.FinishedAtUtc = _now().ToUniversalTime();
                    matchup.Result = won ? "win" : "loss";
                    _recordMatchup(matchup);
                }
            }
            catch (Exception e)
            {
                Logger.Warning($"[Arena] Could not queue the matchup record: {e.Message}");
            }
        }
        var closed = false;
        yield return Ui.ClickUntil(Ui.Find(Result + "/bg/closeButton"), () => !Ui.Visible(Result),
            value => closed = value, "arena result", 1f, 4f);
        if (!closed)
        {
            Retry("The Arena result could not be closed; no further fight was attempted.");
            yield break;
        }

        Logger.Info($"[Arena] Fight against {target.Name} completed: {(won ? "won" : "lost")}.");
        _fought = true;
        NextRunTime = _now().AddMinutes(1);
    }

    private bool CanStartFight(Opponent target, bool forced, int expectedSquad, double reserve,
        out double myPower, out double theirPower, out bool usedDailyException)
    {
        myPower = 0;
        theirPower = 0;
        usedDailyException = false;
        if (!HasTime(reserve, 10))
        {
            Logger.Warning("[Arena] Not enough task runtime remains to start a fight and restore the squad.");
            return false;
        }
        if (Ui.Visible(Result) || _margin.Value <= 0 ||
            !PreviewMatches(target, out myPower, out theirPower) ||
            (expectedSquad != 0 && Planner.SquadApplier.ActiveSlot() != expectedSquad))
            return false;
        if (ArenaPowerPolicy.WithinLimit(myPower, theirPower, _margin.Value)) return true;
        if (!forced || !CanForceToday()) return false;

        var previous = _lastForced.Value;
        _lastForced.Value = _now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try { _settings.SaveToFile(); }
        catch (Exception e)
        {
            _lastForced.Value = previous;
            Logger.Warning($"[Arena] Could not save the daily fight attempt: {e.Message}");
            return false;
        }
        usedDailyException = true;
        return true;
    }

    private bool CanForceToday()
    {
        var now = _now();
        var refresh = Ui.Find(Refresh);
        return _dailyFight.Value && now.Hour >= 20 &&
               _lastForced.Value != now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
               refresh != null && refresh.gameObject.activeInHierarchy &&
               refresh.TryGetComponent(out Button button) && (!button.enabled || !button.interactable);
    }

    private static bool PreviewMatches(Opponent target, out double myPower, out double theirPower)
    {
        myPower = 0;
        theirPower = 0;
        return Ui.Visible(Preview) &&
               string.Equals(Ui.Text(Defender + "/nameBg/nameText"), target.Name, StringComparison.Ordinal) &&
               ArenaPowerPolicy.TryParseRank(Ui.Text(Defender + "/statsBg/rankingBg/rankingValue"), out var rank) &&
               rank == target.Rank &&
               ArenaPowerPolicy.TryParsePower(Ui.Text(Attacker + "/statsBg/powerBg/powerValue"), out myPower) &&
               ArenaPowerPolicy.TryParsePower(Ui.Text(Defender + "/statsBg/powerBg/powerValue"), out theirPower) &&
               theirPower == target.Power;
    }

    private static bool CardMatches(Opponent target) =>
        target.Card != null && target.Card.parent == Ui.Find(Grid) &&
        string.Equals(Ui.Text(target.Card.Find("nameBg/nameText")), target.Name, StringComparison.Ordinal) &&
        ArenaPowerPolicy.TryParseRank(Ui.Text(target.Card.Find("rankingBg/rankingValue")), out var rank) &&
        rank == target.Rank &&
        ArenaPowerPolicy.TryParsePower(Ui.Text(target.Card.Find("powerBg/powerValue")), out var power) &&
        power == target.Power;

    private static string ResultOpponent() => Ui.Text(Result + "/bg/rankChanges/rightPlayer/nameBg/nameText");

    private static IEnumerator CloseOwnPreview()
    {
        if (!Ui.Visible(Preview)) yield break;
        if (Ui.Clickable(Preview + "/bg/closeButton")) yield return Ui.Click(Preview + "/bg/closeButton");
        if (Ui.Visible(Preview)) Logger.Warning("[Arena] The bot-opened preview could not be closed.");
    }

    private void Retry(string reason)
    {
        Logger.Warning("[Arena] " + reason);
        NextRunTime = _now().AddMinutes(20);
        BubbleQuietUntil = NextRunTime;
    }

    private sealed class Opponent
    {
        public Transform Card;
        public string Name;
        public long Rank;
        public double Power;
    }

    private static List<Opponent> ReadOpponents(out int unreadable, out int clickable)
    {
        var result = new List<Opponent>();
        unreadable = 0;
        clickable = 0;
        foreach (var card in Ui.VisibleChildren(Grid))
        {
            if (!Ui.Clickable(card.Find("fightButton"))) continue;
            clickable++;
            var name = Ui.Text(card.Find("nameBg/nameText"));
            if (string.IsNullOrWhiteSpace(name) ||
                !ArenaPowerPolicy.TryParseRank(Ui.Text(card.Find("rankingBg/rankingValue")), out var rank) ||
                !ArenaPowerPolicy.TryParsePower(Ui.Text(card.Find("powerBg/powerValue")), out var power))
            {
                unreadable++;
                continue;
            }
            result.Add(new Opponent { Card = card, Name = name, Rank = rank, Power = power });
        }
        return result;
    }

    private static string Signature(IEnumerable<Opponent> opponents, int unreadable, int clickable) =>
        string.Join("|", opponents.Select(o => $"{o.Name}\u001f{o.Rank}\u001f{o.Power:R}").OrderBy(s => s)) +
        $":{unreadable}:{clickable}";
}

internal static class ArenaPowerPolicy
{
    private static readonly Regex PowerPattern = new(
        @"^(?<number>(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]+)?)\s*(?<suffix>[KMBT]?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex IntegerPattern = new(
        @"^(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)$", RegexOptions.CultureInvariant);
    private static readonly Regex TokensPattern = new(
        @"^(?<current>[0-9]+)(?:\s*/\s*(?<maximum>[0-9]+))?$", RegexOptions.CultureInvariant);

    public static bool TryParsePower(string text, out double power)
    {
        power = 0;
        var match = PowerPattern.Match((text ?? string.Empty).Trim());
        if (!match.Success ||
            !double.TryParse(match.Groups["number"].Value.Replace(",", ""), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number)) return false;
        var multiplier = match.Groups["suffix"].Value.ToUpperInvariant() switch
        {
            "K" => 1e3,
            "M" => 1e6,
            "B" => 1e9,
            "T" => 1e12,
            _ => 1
        };
        power = number * multiplier;
        return double.IsFinite(power) && power > 0;
    }

    public static bool TryParseFormationPower(string text, out double power)
    {
        text = (text ?? string.Empty).Trim();
        var colon = text.LastIndexOf(':');
        if (colon >= 0)
        {
            var label = text[..colon];
            if (string.IsNullOrWhiteSpace(label) || label.Any(c => char.IsDigit(c) || c == '+' || c == '-'))
            {
                power = 0;
                return false;
            }
            text = text[(colon + 1)..].Trim();
        }
        return TryParsePower(text, out power);
    }

    public static bool TryParseRank(string text, out long rank)
    {
        rank = 0;
        text = (text ?? string.Empty).Trim();
        return IntegerPattern.IsMatch(text) &&
               long.TryParse(text.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out rank) &&
               rank > 0;
    }

    public static bool TryParseTokens(string text, out int count)
    {
        count = 0;
        var match = TokensPattern.Match((text ?? string.Empty).Trim());
        return match.Success &&
               int.TryParse(match.Groups["current"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out count) &&
               (!match.Groups["maximum"].Success ||
                (int.TryParse(match.Groups["maximum"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var maximum) && maximum > 0));
    }

    public static bool WithinLimit(double myPower, double theirPower, int percent) =>
        double.IsFinite(myPower) && myPower > 0 && double.IsFinite(theirPower) && theirPower > 0 &&
        percent > 0 && theirPower / myPower <= percent / 100d;
}
