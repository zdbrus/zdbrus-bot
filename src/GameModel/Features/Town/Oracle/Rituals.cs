using System;
using System.Collections;
using System.Linq;
using Firebot.Core;
using Firebot.GameModel.Base;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;
using Firebot.Utilities;

namespace Firebot.GameModel.Features.Town.Oracle;

public class Rituals : GameElement
{
    public Rituals() : base(Paths.MenusLoc.CanvasLoc.TownLoc.OracleLoc.RitualLoc.Rituals) { }

    public IEnumerator Claim()
    {
        foreach (var child in GetChildren().Where(child => child.IsVisible()))
        {
            var claim = new GameButton(Paths.MenusLoc.CanvasLoc.TownLoc.OracleLoc.RitualLoc.ClaimBtn, child);
            var changed = false;
            yield return claim.ClickUntil(() => !claim.IsClickable(), value => changed = value,
                "Oracle ritual claim", 3f);
            if (changed)
                Logger.Info("[Oracle] Ritual claim control changed; reward receipt is not independently verified.");
        }
    }

    public IEnumerator Start()
    {
        foreach (var child in GetChildren().Where(child => child.IsVisible()))
        {
            var start = new GameButton(Paths.MenusLoc.CanvasLoc.TownLoc.OracleLoc.RitualLoc.StartBtn, child);
            var progress = new GameText(Paths.MenusLoc.CanvasLoc.TownLoc.OracleLoc.RitualLoc.CurrentRunTimeTxt, child);
            var changed = false;
            yield return start.ClickUntil(() => progress.IsVisible() || !start.IsClickable(),
                value => changed = value, "Oracle ritual start", 3f);
            if (changed) Logger.Info("[Oracle] A ritual started.");
        }
    }

    public DateTime CurrentRunTime()
    {
        DateTime? earliest = null;

        foreach (var child in GetChildren().Where(child => child.IsVisible()))
        {
            var dateTime = new GameText(Paths.MenusLoc.CanvasLoc.TownLoc.OracleLoc.RitualLoc.CurrentRunTimeTxt, child);
            if (!dateTime.IsVisible()) continue;
            earliest = dateTime.Time;
            break;
        }

        return earliest ?? NextRunTime();
    }

    private static DateTime NextRunTime()
    {
        var text = new GameText(Paths.MenusLoc.CanvasLoc.TownLoc.OracleLoc.RitualLoc.NextRunTimeTxt).GetParsedText();
        return TimeParser.ParseFromText(text);
    }
}