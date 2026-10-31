using System;
using System.Collections;
using System.Linq;
using Firebot.Core;
using Firebot.GameModel.Base;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;

namespace Firebot.GameModel.Features.Town.Library.FirestoneResearch;

public class ResearchPanel : GameElement
{
    public ResearchPanel() : base(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.Root) { }

    public static bool HasEmptySlot =>
        new GameElement(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.SelectResearchTable)
            .IsVisible();

    public static int ActiveSlotCount => new ResearchPanel().GetChildren()
        .Count(child => child.IsVisible() && child.Name.StartsWith("researchSlot", StringComparison.Ordinal) &&
                        new GameElement("/container/researchInfo", child).IsVisible());

    public IEnumerator Claim()
    {
        foreach (var child in GetChildren())
            if (child.IsVisible() && child.Name.StartsWith("researchSlot"))
            {
                var speedBtn = new GameButton(
                    Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.SpeedupBtn, child);

                var canClaim = !new GameElement(
                    Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.SpeedupFinishDesc, child).IsVisible();

                var claimButton = new GameButton(
                    Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.ClaimBtn, child);
                var hadEmptySlot = HasEmptySlot;
                var claimWasClickable = claimButton.IsClickable();

                if (speedBtn.IsClickable() && canClaim)
                {
                    var changed = false;
                    yield return speedBtn.ClickUntil(
                        () => !speedBtn.IsClickable() || (!hadEmptySlot && HasEmptySlot) ||
                              (!claimWasClickable && claimButton.IsClickable()),
                        value => changed = value, "Firestone research free finish");
                    if (changed)
                    {
                        Debug("[INFO] Research free-finish state changed.");
                        yield break;
                    }
                }

                if (!claimButton.IsClickable()) continue;

                var claimed = false;
                yield return claimButton.ClickUntil(
                    () => !claimButton.IsClickable() || (!hadEmptySlot && HasEmptySlot),
                    value => claimed = value, "Firestone research claim");
                if (claimed)
                {
                    Debug("[INFO] Research claim control or slot state changed.");
                    yield break;
                }
            }
    }

    /// <summary>
    ///     When to come back: shortly before the earliest research ends. The visit is moved 3 minutes earlier only
    ///     when that slot's speed-up is actually free; new accounts don't have free speed-ups yet, and coming back
    ///     early there only reopened the library on every scan until the timer ran out.
    /// </summary>
    public DateTime NextRunTime()
    {
        var now = DateTime.Now;
        var minTime = DateTime.MaxValue;
        foreach (var child in GetChildren())
        {
            if (!child.IsVisible() || !child.Name.StartsWith("researchSlot")) continue;

            var end = new GameText(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.NextRunTimeTxt, child).Time;
            if (end == DateTime.MinValue) continue; // empty or locked slot: no timer to read

            var costText = new GameText(
                Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.ResearchPanelLoc.SpeedupBtn + "/costText", child).GetParsedText();
            var isFreeNow = costText.Trim().Equals("Free", StringComparison.OrdinalIgnoreCase);
            var remaining = (end - now).TotalSeconds;

            DateTime when;
            if (remaining <= BotSettings.FreeSpeedupSeconds)
                when = isFreeNow ? now : end.AddSeconds(5); // no free speed-up: wait for the timer itself
            else
                when = end.AddSeconds(-BotSettings.FreeSpeedupSeconds);

            if (when < minTime) minTime = when;
        }

        // Nothing running (slots locked, nothing left to research): look again later instead of on every scan.
        return minTime == DateTime.MaxValue ? now.AddMinutes(15) : minTime;
    }
}