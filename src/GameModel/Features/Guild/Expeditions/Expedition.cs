using System;
using System.Collections;
using Firebot.Core;
using Firebot.GameModel.Base;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;

namespace Firebot.GameModel.Features.Guild.Expeditions;

public static class Expedition
{
    public static IEnumerator Close =>
        new GameButton(Paths.MenusLoc.CanvasLoc.GuildLoc.ExpeditionLoc.CloseBtn).Click();

    public static bool IsExpeditionActive =>
        new GameElement(Paths.MenusLoc.CanvasLoc.GuildLoc.ExpeditionLoc.ActiveExpedition).IsVisible();

    public static DateTime NextRunTime =>
        new GameText(Paths.MenusLoc.CanvasLoc.GuildLoc.ExpeditionLoc.NextRunTimeTxt).Time;

    public static IEnumerator Claim => ClaimAndVerify();

    private static IEnumerator ClaimAndVerify()
    {
        var button = new GameButton(Paths.MenusLoc.CanvasLoc.GuildLoc.ExpeditionLoc.ClaimBtn);
        if (!button.IsClickable()) yield break;

        var wasActive = IsExpeditionActive;
        var claimed = false;
        yield return button.ClickUntil(
            () => !button.IsClickable() || (wasActive && !IsExpeditionActive),
            value => claimed = value, "Expedition claim", 5f);
        if (claimed)
            Logger.Info("[Expeditions] Claim control or active-expedition state changed; reward receipt is not independently verified.");
    }

    public static DateTime CurrentRunTime =>
        new GameText(Paths.MenusLoc.CanvasLoc.GuildLoc.ExpeditionLoc.CurrentRunTimeTxt).Time;

    public static IEnumerator Start => StartAndVerify();

    private static IEnumerator StartAndVerify()
    {
        if (IsExpeditionActive) yield break;

        var button = new GameButton(Paths.MenusLoc.CanvasLoc.GuildLoc.ExpeditionLoc.StartBtn);
        if (!button.IsClickable()) yield break;

        var started = false;
        yield return button.ClickUntil(() => IsExpeditionActive,
            value => started = value, "Expedition start", 5f);
        if (started) Logger.Info("[Expeditions] An expedition became active.");
    }
}