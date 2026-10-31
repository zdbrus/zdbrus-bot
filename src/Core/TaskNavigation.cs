namespace Firebot.Core;

internal enum TaskHub { None, Town, Guild }

internal static class TaskNavigation
{
    // Only tasks whose opening helpers can reuse an already-open hub belong here.
    internal static TaskHub HubFor(string taskType) => taskType switch
    {
        "AlchemistTask" or "EngineerToolsTask" or "FirestoneResearchTask" or
        "TavernTask" or "ExoticMerchantTask" or "WarMachineUpgradeTask" or
        "OracleBlessingsTask" or "MeteoriteResearchTask" or "TemplePrestigeTask" or
        "ScarabGameTask" or "ScarabMilestonesTask" or "PharaohsVaultTask" or
        "EventExchangeTask" or "ArenaTask" => TaskHub.Town,
        "AwakenHeroesTask" or "ArcaneCrystalTask" or "ChaosRiftTask" or
        "PersonalTreeTask" or "ForbiddenKnowledgeTask" => TaskHub.Guild,
        _ => TaskHub.None
    };

    internal static bool KeepOpen(string closePath, TaskHub hub) =>
        hub == TaskHub.Town && closePath.EndsWith("/menus/TownIrongard/closeButton", System.StringComparison.Ordinal) ||
        hub == TaskHub.Guild && closePath.EndsWith("/menus/TownGuild/closeButton", System.StringComparison.Ordinal);
}
