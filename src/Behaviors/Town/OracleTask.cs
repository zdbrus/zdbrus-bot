using System.Collections;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Features.Town.Oracle;
using Firebot.GameModel.Shared;
using Firebot.Infrastructure;

namespace Firebot.Behaviors.Town;

public class OracleTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.Oracle;

    protected override string NotificationPath => Paths.BattleLoc.NotificationsLoc.OracleRitualsBtn;

    public override int MinimumLevel => LevelRequirements.Oracle;

    public override IEnumerator Execute()
    {
        yield return Notifications.OracleRituals;
        var rituals = new Rituals();
        yield return rituals.Claim();
        yield return rituals.Start();
        NextRunTime = rituals.CurrentRunTime();
        yield return Oracle.Close;
    }
}