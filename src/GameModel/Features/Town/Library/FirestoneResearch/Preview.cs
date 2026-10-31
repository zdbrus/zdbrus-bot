using System;
using System.Collections;
using Firebot.GameModel.Base;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;

namespace Firebot.GameModel.Features.Town.Library.FirestoneResearch;

public static class Preview
{
    public static bool IsVisible => new GameElement(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.Root).IsVisible();

    public static IEnumerator Close(Action<bool> closed) =>
        new GameButton(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.Root + "/bg/closeButton")
            .ClickUntil(() => !IsVisible, closed, "close Firestone research preview");

    public static bool IsUnlocked =>
        new GameText(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.UnlockedTxt).IsVisible();

    public static bool IsMaxed =>
        new GameText(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.MaxedTxt).IsVisible();

    public static IEnumerator Start(Action<bool> started)
    {
        if (!ResearchPanel.HasEmptySlot)
        {
            started(false);
            yield break;
        }

        var activeBefore = ResearchPanel.ActiveSlotCount;
        var button = new GameButton(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.PreviewLoc.ActivateBtn);
        yield return button.ClickUntil(() => !ResearchPanel.HasEmptySlot || ResearchPanel.ActiveSlotCount > activeBefore,
            value =>
            {
                if (value) Firebot.Core.Logger.Info("[Research] A research slot was consumed.");
                started(value);
            }, "Firestone research start");
    }
}