using System.Collections;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Features.Town.Engineer;
using Firebot.GameModel.Shared;
using Firebot.Infrastructure;

namespace Firebot.Behaviors.Town;

public class EngineerToolsTask : BotTask
{
    protected override string NotificationPath => Paths.BattleLoc.NotificationsLoc.EngineerBtn;

    public override int MinimumLevel => LevelRequirements.EngineerTools;

    public override IEnumerator Execute()
    {
        yield return Notifications.Engineer;
        const string claimButton =
            P.Menus + "/Engineer/submenus/bg/engineerSubmenu/toolsProductionSection/claimToolsButton";
        const string toolsQuantity = P.Menus + "/Engineer/counters/currencyInteraction (Tools)/quantity";
        var before = Ui.NumberAt(toolsQuantity);
        var confirmed = false;
        yield return Ui.ClickUntil(Ui.Find(claimButton), () => Ui.NumberAt(toolsQuantity) > before,
            value => confirmed = value, "Engineer tools claim", 1.5f);
        if (confirmed)
            Logger.Info($"[Engineer] Tools increased from {before:0} to {Ui.NumberAt(toolsQuantity):0}.");
        else
            Logger.Warning("[Engineer] Tools claim did not increase the tools counter.");
        NextRunTime = Engineer.NextRunTime;
        yield return Engineer.Close;
    }
}