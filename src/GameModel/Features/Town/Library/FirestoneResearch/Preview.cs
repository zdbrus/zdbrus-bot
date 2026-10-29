using System.Collections;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;

namespace Firebot.GameModel.Features.Town.Library.FirestoneResearch;

public static class Preview
{
    public static bool IsUnlocked =>
        new GameText(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.UnlockedTxt).IsVisible();

    public static bool IsMaxed =>
        new GameText(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.MaxedTxt).IsVisible();

    public static IEnumerator Start => StartAndVerify();

    private static IEnumerator StartAndVerify()
    {
        if (!ResearchPanel.HasEmptySlot) yield break;

        var button = new GameButton(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.ActivateBtn);
        yield return button.ClickUntil(() => !ResearchPanel.HasEmptySlot,
            value =>
            {
                if (value) Firebot.Core.Logger.Info("[Research] A research slot was consumed.");
            }, "Firestone research start");
    }
}