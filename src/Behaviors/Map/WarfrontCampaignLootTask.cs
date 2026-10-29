using System;
using System.Collections;
using System.Collections.Generic;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Features.Map;
using Firebot.GameModel.Features.Map.WarfrontCampaign;
using Firebot.GameModel.Shared;

namespace Firebot.Behaviors.Map;

public class WarfrontCampaignLootTask : BotTask
{
    public override int MinimumLevel => LevelRequirements.WarfrontCampaign;
    internal override IEnumerable<string> HandledNotificationNames => new[] { "WarfrontCampaign" };

    protected override bool IsNotificationVisibleCore()
        => IsEnabled && DateTime.Now >= BubbleQuietUntil && DateTime.Now >= NextRunTime &&
           Ui.Visible(P.Notifications + "/WarfrontCampaign");

    public override IEnumerator Execute()
    {
        yield return Notifications.WarfrontCampaign;
        const string claimButton =
            P.Menus + "/WorldMap/submenus/warfrontCampaignSubmenu/loot/claimButton";
        const string honor = P.Menus + "/WorldMap/counters/currencyInteraction (Honor)/quantity";
        const string gems = P.Menus + "/WorldMap/counters/currencyInteraction (Gem)/quantity";
        var honorBefore = Ui.NumberAt(honor);
        var gemsBefore = Ui.NumberAt(gems);
        var stateChanged = false;
        yield return Ui.ClickUntil(Ui.Find(claimButton),
            () => Ui.NumberAt(honor) > honorBefore || Ui.NumberAt(gems) > gemsBefore ||
                  !Ui.Clickable(claimButton),
            value => stateChanged = value, "Warfront campaign loot", 1.5f);
        yield return Ui.CloseRewards();
        if (stateChanged)
        {
            if (Ui.NumberAt(honor) > honorBefore || Ui.NumberAt(gems) > gemsBefore)
                Logger.Info("[Warfront] A campaign currency counter increased after the loot claim.");
            else
                Logger.Info("[Warfront] Claim state changed; reward receipt is not independently verified.");
        }
        else
            Logger.Warning("[Warfront] Loot claim produced no observable state change.");
        if (stateChanged)
            NextRunTime = WarfrontLoot.NextRunTime;
        else
        {
            var retry = WarfrontLoot.NextRunTime;
            if (retry <= DateTime.Now) retry = DateTime.Now.AddMinutes(15);
            NextRunTime = retry;
            BubbleQuietUntil = retry;
        }

        yield return MapMission.Close;
    }
}