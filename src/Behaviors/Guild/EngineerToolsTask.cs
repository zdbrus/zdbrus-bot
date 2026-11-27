using System;
using System.Collections;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Features.Guild.Expeditions;
using Firebot.GameModel.Shared;
using Firebot.Infrastructure;

namespace Firebot.Behaviors.Guild;

public class ExpeditionTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.Guilds;

    protected override string NotificationPath => Paths.BattleLoc.NotificationsLoc.ExpeditionsBtn;

    public override IEnumerator Execute()
    {
        yield return Notifications.Expeditions;
        yield return Expedition.Claim;
        yield return Expedition.Start;

        // The timer text fills in a moment after the screen changes. Read it only once it parses, so a blank label
        // never becomes a guess (issue #10: a fixed 7 hours instead of the real expedition length).
        var read = DateTime.MinValue;
        yield return Ui.WaitUntil(() =>
        {
            read = Expedition.IsExpeditionActive ? Expedition.CurrentRunTime : Expedition.NextRunTime;
            return read > DateTime.Now;
        }, _ => { }, 5f);

        if (read > DateTime.Now)
            NextRunTime = read;
        else
        {
            Logger.Warning("[Expeditions] Could not read the expedition timer; checking again in 10 minutes.");
            NextRunTime = DateTime.Now.AddMinutes(10);
        }

        yield return Expedition.Close;
    }
}