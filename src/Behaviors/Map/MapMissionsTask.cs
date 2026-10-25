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
                    var progress = MissionPreview.NextRunTime;
                    if (!earliest.HasValue || progress < earliest.Value) earliest = progress;
                }
            }

            yield return MissionRewardsPopup.Close;
            yield return MissionPreview.Close;
        }

        // Start new missions only while squads are free. With every squad busy there is nothing to start,
        // so the whole list is skipped instead of opening each mission to read "not enough squads".
        var free = FreeSquads;
        if (free != 0)
        {
            foreach (var mission in ScanMissions(m => !m.IsActive && !m.IsCompleted, true).ToList())
            {
                if (FreeSquads == 0) break;

                yield return mission.Select();

                if (MissionPreview.IsNotEnoughSquads)
                {
                    yield return MissionPreview.Close;
                    continue;
                }

                var endsAt = mission.TimeRequired;
                yield return MissionPreview.StartMission;
                if (endsAt > DateTime.Now && (!earliest.HasValue || endsAt < earliest.Value)) earliest = endsAt;
            }
        }
        else
            Debug("All squads are busy; skipping new missions.");

        // The earliest end among running and just-started missions decides the next run. With nothing
        // running, fall back to the map's own refresh time.
        NextRunTime = earliest?.AddSeconds(-BotSettings.FreeSpeedupSeconds) ?? MapMission.NextRunTime;

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