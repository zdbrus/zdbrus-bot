using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Base;
using Firebot.GameModel.Features.Map;
using Firebot.GameModel.Features.Map.Missions;
using Firebot.GameModel.Shared;
using Firebot.Infrastructure;
using MelonLoader;

namespace Firebot.Behaviors.Map;

public class MapMissionsTask : BotTask
{
    private MelonPreferences_Entry<string> _timeOrder;

    protected override string NotificationPath => Paths.BattleLoc.NotificationsLoc.MapMissionsBtn;

    protected override void OnConfigure(MelonPreferences_Category category)
    {
        if (_timeOrder != null) return;

        _timeOrder = category.CreateEntry(
            "mission_time_order",
            "desc",
            "Priority",
            "Sort missions by time required. Use 'asc' (shorter first) or 'desc' (longer first)."
        );
    }

    private const string SquadsText = Firebot.Behaviors.Extra.P.Menus + "/WorldMap/counters/squads/quantity";

    /// <summary>Free squads, read from the "free/total" counter at the top of the map. -1 when unreadable.</summary>
    private static double FreeSquads => Firebot.Behaviors.Extra.Ui.NumberAt(SquadsText);

    public override IEnumerator Execute()
    {
        yield return Notifications.MapMissions;

        // One pass over the running missions: claim the finished ones, finish the free ones, and note when
        // the next one ends. (This used to be two full passes that opened every running mission twice.)
        DateTime? earliest = null;
        foreach (var mission in ScanMissions(m => m.IsActive).ToList())
        {
            var wasCompleted = mission.IsCompleted;
            yield return mission.Select();

            if (!wasCompleted)
            {
                var speedupBtn = MissionPreview.SpeedupBtn;
                if (speedupBtn.IsVisible() && MissionPreview.CanSpeedup)
                    yield return speedupBtn.Click();
                else
                {
                    // Come back 3 minutes early for the free speed-up, unless this mission is already inside those
                    // 3 minutes and its speed-up still isn't free (new accounts): then wait for the real end, or the
                    // map is reopened on every scan until the timer runs out.
                    var progress = MissionPreview.NextRunTime;
                    var inWindow = (progress - DateTime.Now).TotalSeconds <= BotSettings.FreeSpeedupSeconds;
                    var when = inWindow ? progress.AddSeconds(5) : progress.AddSeconds(-BotSettings.FreeSpeedupSeconds);
                    if (progress != DateTime.MinValue && (!earliest.HasValue || when < earliest.Value)) earliest = when;
                }
            }

            yield return MissionRewardsPopup.Close;
            yield return MissionPreview.Close;
        }

        // Start new missions only while squads are free. With every squad busy there is nothing to start,
        // so the whole list is skipped instead of opening each mission to read "not enough squads".
        var free = FreeSquads;
        if (free > 0)
        {
            foreach (var mission in ScanMissions(m => !m.IsActive && !m.IsCompleted, true).ToList())
            {
                if (FreeSquads <= 0) break;

                yield return mission.Select();

                if (MissionPreview.IsNotEnoughSquads)
                {
                    yield return MissionPreview.Close;
                    continue;
                }

                var endsAt = mission.TimeRequired;
                var squadsBefore = FreeSquads;
                yield return MissionPreview.StartMission;
                var started = false;
                yield return Firebot.Behaviors.Extra.Ui.WaitUntil(
                    () => mission.IsActive || (squadsBefore >= 0 && FreeSquads < squadsBefore),
                    value => started = value, 5f);
                if (!started)
                {
                    Logger.Warning("[Map Missions] Start control produced no active-mission or free-squad change; stopping new starts.");
                    yield return MissionPreview.Close;
                    break;
                }

                var due = endsAt.AddSeconds(-BotSettings.FreeSpeedupSeconds);
                if (due < DateTime.Now) due = endsAt;
                if (endsAt > DateTime.Now && (!earliest.HasValue || due < earliest.Value)) earliest = due;
            }
        }
        else if (free < 0)
            Logger.Warning("[Map Missions] Could not read the free-squad counter; skipping mission starts.");
        else
            Debug("All squads are busy; skipping new missions.");

        // The earliest end among running and just-started missions decides the next run. With nothing
        // running, fall back to the map's own refresh time.
        NextRunTime = earliest ?? MapMission.NextRunTime;
        if (NextRunTime < DateTime.Now.AddSeconds(20)) NextRunTime = DateTime.Now.AddSeconds(20);

        yield return MapMission.Close;
    }

    private IEnumerable<MissionPin> ScanMissions(Func<MissionPin, bool> filter = null, bool sortByTime = false)
    {
        var missionRoot = new GameElement(Paths.MenusLoc.CanvasLoc.MapLoc.MissionsLoc.PinLoc.Root);
        var results = missionRoot.GetChildren().Where(root => root.IsVisible())
            .SelectMany(parent => parent.GetChildren().Where(child => child.IsVisible()))
            .Select(pin => new MissionPin(parent: pin))
            .Where(mission => filter == null || filter(mission))
            .ToList();

        if (sortByTime)
            results = IsAscending()
                ? results.OrderBy(m => m.TimeRequired).ToList()
                : results.OrderByDescending(m => m.TimeRequired).ToList();

        foreach (var mission in results) yield return mission;
    }

    private bool IsAscending()
    {
        var value = _timeOrder?.Value?.Trim();
        if (string.IsNullOrEmpty(value)) return false;

        if (string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(value, "desc", StringComparison.OrdinalIgnoreCase)) return false;

        Debug($"[FAILED] Invalid mission_time_order '{value}'. Using default 'desc'.");
        return false;
    }
}