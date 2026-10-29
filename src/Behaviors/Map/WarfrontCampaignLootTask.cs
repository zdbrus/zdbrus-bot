using System;
using System.Collections;
using System.Collections.Generic;
using Firebot.Behaviors.Extra;
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
        => IsEnabled && DateTime.Now >= BubbleQuietUntil && Ui.Visible(P.Notifications + "/WarfrontCampaign");

    public override IEnumerator Execute()
    {
        yield return Notifications.WarfrontCampaign;
        yield return WarfrontLoot.Claim;
        NextRunTime = WarfrontLoot.NextRunTime;
        yield return MapMission.Close;
    }
}