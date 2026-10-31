using System;
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
    protected override bool UseScheduledTime => true;
    private const string Menu = P.Menus + "/Engineer";
    private const string Tools = Menu + "/submenus/bg/engineerSubmenu";

    public override int MinimumLevel => LevelRequirements.EngineerTools;

    public override IEnumerator Execute()
    {
        if (!Ui.Visible(Tools))
        {
            if (Ui.Visible(NotificationPath))
            {
                yield return Notifications.Engineer;
                yield return Ui.WaitFor(Tools, 2f);
            }

            if (!Ui.Visible(Tools))
                yield return Ui.OpenTownSubmenu("engineer", Tools, Menu + "/submenus/submenuButtons/engineer");
        }

        if (!Ui.Visible(Tools))
        {
            Logger.Warning("[Engineer] Could not open the tools screen.");
            NextRunTime = DateTime.Now.AddMinutes(15);
            if (Ui.Visible(Menu)) yield return Engineer.Close;
            yield break;
        }

        const string claimButton =
            P.Menus + "/Engineer/submenus/bg/engineerSubmenu/toolsProductionSection/claimToolsButton";
        const string toolsQuantity = P.Menus + "/Engineer/counters/currencyInteraction (Tools)/quantity";
        var before = Ui.NumberAt(toolsQuantity);
        if (before < 0)
        {
            Logger.Warning("[Engineer] Could not read the tools counter; no claim was attempted.");
            NextRunTime = DateTime.Now.AddMinutes(15);
            yield return Engineer.Close;
            yield break;
        }

        if (!Ui.Clickable(claimButton))
        {
            Logger.Info("[Engineer] Tools are not ready to claim.");
            NextRunTime = Engineer.NextRunTime;
            yield return Engineer.Close;
            yield break;
        }

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