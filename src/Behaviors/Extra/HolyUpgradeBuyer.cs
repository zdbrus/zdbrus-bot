using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebot.Core;
using Il2Cpp;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

internal static class HolyUpgradeBuyer
{
    private const string MQ = P.Menus + "/MagicQuarters";
    private const string Holy = MQ + "/submenus/bg/chaosRiftSubmenu/holyDamageUpgrade/bg/upgradeButton";
    private const string HolyLevel = MQ + "/submenus/bg/chaosRiftSubmenu/holyDamageUpgrade/bg/upgradeNumBg/upgradeNum";
    private const string Orbs = MQ + "/counters/currencyInteraction (OrbOfLight)/quantity";
    private const string LockedGuardian = P.Popups + "/LockedGuardian";

    public static IEnumerator Run(string priorities, UncappedUpgradeMode mode, Action retrySoon)
    {
        var wanted = (priorities ?? string.Empty).Split(',')
            .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0) yield break;

        var guardians = new List<(Transform tile, string name, bool unlocked)>();
        foreach (var tile in Ui.VisibleChildren(MQ + "/guardianList"))
        {
            var interaction = tile.GetComponent<GuardianInteractionSquare>();
            if (interaction != null && interaction.isEmpty) continue;
            var guardian = interaction?.guardian;
            if (guardian == null || string.IsNullOrWhiteSpace(guardian.guardianName))
            {
                Logger.Warning("[Chaos Rift] A guardian's identity is unreadable; holy upgrades were left untouched.");
                yield break;
            }
            guardians.Add((tile, guardian.guardianName, guardian.IsUnlocked.GetDecrypted()));
        }

        var done = 0;
        foreach (var name in wanted)
        {
            var guardian = guardians.FirstOrDefault(g => g.name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (guardian.tile == null)
            {
                Logger.Warning($"[Chaos Rift] Guardian {name} was not found; lower priorities were left untouched.");
                break;
            }
            if (!guardian.unlocked) continue;

            var selected = false;
            yield return Ui.ClickUntil(guardian.tile,
                () => Ui.Visible(LockedGuardian) ||
                      (guardian.tile.Find("highlighted") != null &&
                       guardian.tile.Find("highlighted").gameObject.activeInHierarchy &&
                       Ui.Text(MQ + "/guardianPreview/info/guardianName").Equals(name, StringComparison.OrdinalIgnoreCase)),
                value => selected = value, $"select guardian {name}", 1.2f);
            if (Ui.Visible(LockedGuardian))
            {
                yield return Ui.Click(LockedGuardian + "/bg/closeButton", 0.8f);
                continue;
            }
            if (!selected)
            {
                Logger.Warning($"[Chaos Rift] Selection of {name} was not confirmed; lower priorities were left untouched.");
                break;
            }
            yield return Ui.Click(MQ + "/submenus/submenuButtons/chaosRift", 1.2f);
            if (!Ui.Visible(MQ + "/submenus/bg/chaosRiftSubmenu"))
            {
                Logger.Warning($"[Chaos Rift] Holy upgrades for {name} did not open; lower priorities were left untouched.");
                break;
            }

            var attempted = 0;
            var failed = false;
            for (; attempted < 10; attempted++)
            {
                var cost = Ui.NumberAt(Holy + "/layout/costText");
                var orbsBefore = Ui.NumberAt(Orbs);
                if (cost <= 0 || orbsBefore < 0)
                {
                    Logger.Warning($"[Chaos Rift] {name}: holy upgrade cost or orb count is unreadable.");
                    failed = true;
                    break;
                }
                if (orbsBefore < cost || !Ui.Clickable(Holy)) break;
                var levelBefore = Ui.Text(HolyLevel);
                var upgraded = false;
                yield return Ui.ClickUntil(Ui.Find(Holy),
                    () => PurchaseObservation.Decreased(orbsBefore, Ui.NumberAt(Orbs)) ||
                          PurchaseObservation.LevelIncreased(levelBefore, Ui.Text(HolyLevel)) ||
                          PurchaseObservation.ChangedPrice(cost, Ui.NumberAt(Holy + "/layout/costText")),
                    value => upgraded = value, "holy damage upgrade", 1.2f, 2f);
                if (!upgraded)
                {
                    Logger.Warning($"[Chaos Rift] {name}: the holy upgrade was not confirmed.");
                    failed = true;
                    break;
                }
                done++;
            }
            if (failed) break;
            if (attempted == 10 && mode == UncappedUpgradeMode.Focus &&
                Ui.NumberAt(Holy + "/layout/costText") > 0 &&
                Ui.NumberAt(Orbs) >= Ui.NumberAt(Holy + "/layout/costText") && Ui.Clickable(Holy))
                retrySoon();
            if (!UncappedUpgradePolicy.ContinueAfterTarget(mode, true)) break;
        }

        Logger.Info($"[Chaos Rift] Holy upgrades confirmed: {done}; orbs of light now {Ui.NumberAt(Orbs):0}.");
    }
}
