using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Planner;

/// <summary>
///     "Apply to game" for the War Machine Planner: sets the active squad to the planned machines and crews by going
///     through the same Engineer screens a player uses (Engineer, Squads, Edit, Select crew, Save changes).
///     <para>
///         It waits for the bot to finish the job it is on and keeps new jobs from starting until it is done, so the
///         two never click over each other. Afterwards it reads the squad back from the game and reports what stuck.
///     </para>
/// </summary>
public static class SquadApplier
{
    private const string Engineer = P.Menus + "/Engineer";
    private const string SquadsTab = Engineer + "/submenus/submenuButtons/squads";
    private const string Squads = P.Popups + "/SelectWarMachineFormation";
    private const string EditActive = Squads + "/bg/activeSquad/activeSquadFormationInteraction/editButton";
    private const string Builder = P.Menus + "/SelectWarMachines";
    private const string CrewPicker = P.Popups + "/SelectWarMachineHeroes";
    private const float Step = 0.6f;

    public sealed class PlannedMachine
    {
        public string Code;
        public List<int> Crew = new();
    }

    /// <summary>True from the moment a plan is accepted until the screens are closed again.</summary>
    public static bool Busy { get; private set; }

    /// <summary>Plain-words progress for the planner page.</summary>
    public static string Status { get; private set; } = "";

    public static bool Ok { get; private set; }

    /// <summary>Parses "Judgement:2.3.5,Goliath:0.8" (machine code, then hero ids) from the planner page.</summary>
    public static List<PlannedMachine> Parse(string plan)
    {
        var result = new List<PlannedMachine>();
        if (string.IsNullOrEmpty(plan)) return result;
        foreach (var part in plan.Split(',', StringSplitOptions.RemoveEmptyEntries).Take(5))
        {
            var bits = part.Split(':');
            var code = bits[0].Trim();
            if (code.Length == 0 || code.Length > 30 || !code.All(char.IsLetterOrDigit)) continue;
            var pm = new PlannedMachine { Code = code };
            if (bits.Length > 1)
                foreach (var id in bits[1].Split('.', StringSplitOptions.RemoveEmptyEntries).Take(6))
                    if (int.TryParse(id, out var n)) pm.Crew.Add(n);
            result.Add(pm);
        }
        return result;
    }

    public static string Request(List<PlannedMachine> plan)
    {
        if (Busy) return "Already applying a squad.";
        if (plan.Count == 0) return "The plan is empty.";
        Busy = true;
        Ok = false;
        Status = "Waiting for the bot to finish its current job…";
        MelonCoroutines.Start(Run(plan));
        return "Applying";
    }

    private static IEnumerator Run(List<PlannedMachine> plan)
    {
        var giveUp = Time.unscaledTime + 180f;
        while (BotManager.IsBusy && Time.unscaledTime < giveUp) yield return new WaitForSeconds(0.5f);

        yield return Watchdog.ForceClearAll("before applying the planner squad");
        yield return Open();
        if (!Ui.Visible(Builder))
        {
            Finish(false, "Could not open the squad editor (Engineer, Squads, Edit). Nothing was changed.");
            yield break;
        }

        Status = "Choosing war machines…";
        yield return SetMachines(plan);
        var mech = Mech();
        if (!OrderMatches(plan))
        {
            // Clicking the deck did not give the planned line-up: set it through the screen's own methods.
            Logger.Info($"[Planner] Deck clicks gave [{string.Join(", ", UiOrder())}], setting the squad directly.");
            if (mech != null)
            {
                try
                {
                    mech.EmptyFormationSpots();
                    foreach (var p in plan)
                    {
                        var wm = FindMachine(p.Code);
                        if (wm != null) mech.AddWarMachineInSpotAuto(wm);
                    }
                }
                catch (Exception e) { Logger.Warning($"[Planner] Direct squad set failed: {e.Message}"); }
                yield return new WaitForSeconds(1f);
            }
        }

        Status = "Setting crews…";
        // Two passes: first take off everyone who should not be on a machine, then add the planned heroes. That
        // frees heroes who are moving from one machine to another before anyone asks for them.
        yield return SetCrews(plan, removeOnly: true);
        yield return SetCrews(plan, removeOnly: false);

        // Crews the picker did not take are set directly on the spot.
        if (mech != null)
        {
            var spots = UnityEngine.Object.FindObjectOfType<SelectWarMachinesViewController>()?.WarMachineFormationSettingSpots;
            for (var s = 0; spots != null && s < spots.Count; s++)
            {
                var spot = spots[s];
                if (spot?.warMachine == null) continue;
                var planned = plan.FirstOrDefault(p => Same(p.Code, spot.warMachine.machineCode.ToString()));
                if (planned == null || planned.Crew.Count == 0) continue;
                var crew = new List<int>();
                for (var i = 0; spot.warMachine.crewHeroIds != null && i < spot.warMachine.crewHeroIds.Count; i++)
                    crew.Add(spot.warMachine.crewHeroIds[i]);
                if (planned.Crew.All(crew.Contains) && crew.All(planned.Crew.Contains)) continue;
                try
                {
                    var list = new Il2CppSystem.Collections.Generic.List<int>();
                    foreach (var id in planned.Crew) list.Add(id);
                    mech.SetSpotCrew(spot.spotIndex, list);
                }
                catch (Exception e) { Logger.Warning($"[Planner] Direct crew set failed: {e.Message}"); }
                yield return new WaitForSeconds(0.3f);
            }
        }

        Status = "Saving…";
        var before = string.Join(", ", UiOrder());
        Logger.Info($"[Planner] Squad on screen before saving: [{before}]");
        var vc = UnityEngine.Object.FindObjectOfType<SelectWarMachinesViewController>();
        var save = vc?.SaveChangesButton;
        if (save != null && save.gameObject.activeInHierarchy && save.interactable) save.onClick.Invoke();
        else if (mech != null)
        {
            try { mech.SaveChanges(); }
            catch (Exception e) { Logger.Warning($"[Planner] SaveChanges failed: {e.Message}"); }
        }
        // The game saves through its server: wait for the reply before closing anything.
        var until = Time.unscaledTime + 10f;
        yield return new WaitForSeconds(1f);
        while (mech != null && Time.unscaledTime < until && SafeWaiting(mech)) yield return new WaitForSeconds(0.25f);
        yield return new WaitForSeconds(1f);

        yield return Watchdog.ForceClearAll("after applying the planner squad");
        // The saved squad can take a moment to show up in the game's data.
        for (var t = 0; t < 8 && !Matches(plan); t++) yield return new WaitForSeconds(1f);
        Verify(plan, before);
    }

    private static IEnumerator Open()
    {
        Status = "Opening the Engineer…";
        yield return Ui.OpenTownBuilding("engineer");
        yield return Ui.Click(P.Popups + "/GarageSelection/bg/engineer", 1.5f);
        yield return Ui.WaitFor(Engineer);
        yield return Ui.Click(SquadsTab, 1.5f);
        yield return Ui.WaitFor(Squads);
        yield return Ui.Click(EditActive, 2f);
        yield return Ui.WaitFor(Builder);
        if (Ui.Visible(Builder)) yield break;

        // The click route missed a step (a slow screen, a popup in the way): open the squad editor directly.
        Logger.Info("[Planner] Opening the squad editor directly.");
        try { Mech()?.Open(null); }
        catch (Exception e) { Logger.Warning($"[Planner] Direct open failed: {e.Message}"); }
        yield return Ui.WaitFor(Builder, 4f);
    }

    private static IEnumerator SetMachines(List<PlannedMachine> plan)
    {
        var vc = UnityEngine.Object.FindObjectOfType<SelectWarMachinesViewController>();
        if (vc == null) yield break;
        var deck = vc.WarMachineSelectInteractions;
        if (deck == null) yield break;

        var wanted = plan.Select(p => p.Code).ToList();
        if (CurrentOrder(vc).SequenceEqual(wanted, StringComparer.OrdinalIgnoreCase)) yield break;

        // Take every machine out, then put the planned ones back in order: the first one picked fills the
        // front spot, so the squad ends up in the planned order.
        for (var i = 0; i < deck.Count; i++)
        {
            var tile = deck[i];
            if (tile == null || tile.warMachine == null || !tile.IsSelected()) continue;
            Press(tile);
            yield return new WaitForSeconds(Step);
        }

        foreach (var code in wanted)
        {
            for (var i = 0; i < deck.Count; i++)
            {
                var tile = deck[i];
                if (tile?.warMachine == null || !Same(tile.warMachine.machineCode.ToString(), code)) continue;
                if (!tile.IsSelected()) Press(tile);
                yield return new WaitForSeconds(Step);
                break;
            }
        }
    }

    private static IEnumerator SetCrews(List<PlannedMachine> plan, bool removeOnly)
    {
        var vc = UnityEngine.Object.FindObjectOfType<SelectWarMachinesViewController>();
        var spots = vc?.WarMachineFormationSettingSpots;
        if (spots == null) yield break;

        for (var s = 0; s < spots.Count; s++)
        {
            var spot = spots[s];
            if (spot?.warMachine == null) continue;
            var planned = plan.FirstOrDefault(p => Same(p.Code, spot.warMachine.machineCode.ToString()));
            if (planned == null) continue;

            var current = new List<int>();
            if (spot.warMachine.crewHeroIds != null)
                for (var i = 0; i < spot.warMachine.crewHeroIds.Count; i++) current.Add(spot.warMachine.crewHeroIds[i]);
            if (removeOnly && current.All(planned.Crew.Contains)) continue;
            if (!removeOnly && planned.Crew.All(current.Contains) && current.All(planned.Crew.Contains)) continue;

            var open = spot.editCrewButton != null && spot.editCrewButton.gameObject.activeInHierarchy
                ? spot.editCrewButton
                : spot.addCrewButton;
            if (open == null || !open.gameObject.activeInHierarchy) continue;
            open.onClick.Invoke();
            yield return Ui.WaitFor(CrewPicker, 3f);
            yield return new WaitForSeconds(Step);

            var picker = UnityEngine.Object.FindObjectOfType<SelectWarMachineHeroesViewController>();
            var heroes = picker?.HeroInteractionSelects;
            if (heroes != null)
            {
                // Off first, then on, so a full crew has room for the new heroes.
                for (var i = 0; i < heroes.Count; i++)
                {
                    var h = heroes[i];
                    if (h?.hero == null || h.isEmpty || !h.IsSelected() || planned.Crew.Contains(h.hero.code)) continue;
                    h.InvokeAction();
                    yield return new WaitForSeconds(0.3f);
                }

                if (!removeOnly)
                    foreach (var id in planned.Crew)
                        for (var i = 0; i < heroes.Count; i++)
                        {
                            var h = heroes[i];
                            if (h?.hero == null || h.isEmpty || h.hero.code != id) continue;
                            if (!h.IsSelected()) h.InvokeAction();
                            yield return new WaitForSeconds(0.3f);
                            break;
                        }
            }

            var set = picker?.SetCrewButton;
            if (set != null && set.gameObject.activeInHierarchy && set.interactable)
            {
                set.onClick.Invoke();
                yield return new WaitForSeconds(1f);
            }
            if (Ui.Visible(CrewPicker)) yield return Ui.Click(CrewPicker + "/bg/closeButton", 1f);
        }
    }

    private static SelectWarMachinesMech Mech()
    {
        try { return GameInitialize.MenuLoader?.SelectWarMachinesMech; }
        catch { return null; }
    }

    private static bool SafeWaiting(SelectWarMachinesMech mech)
    {
        try { return mech.WaitingServerReply; }
        catch { return false; }
    }

    private static WarMachine FindMachine(string code)
    {
        var list = GameInitialize.HandlerLoader?.warMachineHandler?.warMachineData?.warMachineList;
        for (var i = 0; list != null && i < list.Count; i++)
            if (list[i] != null && Same(list[i].machineCode.ToString(), code)) return list[i];
        return null;
    }

    /// <summary>True when a saved squad entry (internal machine code) is the planned machine (by name).</summary>
    private static bool IsMachine(string savedCode, string plannedName)
    {
        if (Same(savedCode, plannedName)) return true;
        var wm = FindMachine(plannedName);
        return wm != null && (Same(wm.code, savedCode) || Same(wm.id, savedCode));
    }

    private static string NameOf(string savedCode)
    {
        var list = GameInitialize.HandlerLoader?.warMachineHandler?.warMachineData?.warMachineList;
        for (var i = 0; list != null && i < list.Count; i++)
            if (list[i] != null && (Same(list[i].code, savedCode) || Same(list[i].id, savedCode)))
                return list[i].machineCode.ToString();
        return savedCode;
    }

    private static List<string> UiOrder()
    {
        var vc = UnityEngine.Object.FindObjectOfType<SelectWarMachinesViewController>();
        return vc == null ? new List<string>() : CurrentOrder(vc);
    }

    private static bool OrderMatches(List<PlannedMachine> plan)
    {
        var have = UiOrder();
        return plan.All(p => have.Any(h => Same(h, p.Code)));
    }

    /// <summary>True when every planned machine is in the saved squad.</summary>
    private static bool Matches(List<PlannedMachine> plan)
    {
        var data = GameInitialize.HandlerLoader?.warMachineHandler?.warMachineData?.formationList;
        if (data == null) return false;
        var codes = new List<string>();
        for (var i = 0; i < data.Count; i++) if (data[i] != null) codes.Add(data[i].code);
        return plan.All(p => codes.Any(c => IsMachine(c, p.Code)));
    }

    private static void Press(WarMachineSelectInteraction tile)
    {
        var b = tile.clickButton;
        if (b != null && b.gameObject.activeInHierarchy && b.interactable) b.onClick.Invoke();
        else tile.ClickAction();
    }

    private static List<string> CurrentOrder(SelectWarMachinesViewController vc)
    {
        var spots = vc.WarMachineFormationSettingSpots;
        var list = new List<(int, string)>();
        for (var i = 0; spots != null && i < spots.Count; i++)
            if (spots[i]?.warMachine != null) list.Add((spots[i].spotIndex, spots[i].warMachine.machineCode.ToString()));
        return list.OrderBy(x => x.Item1).Select(x => x.Item2).ToList();
    }

    /// <summary>Reads the active squad back from the game and says what matched.</summary>
    private static void Verify(List<PlannedMachine> plan, string onScreen)
    {
        try
        {
            var data = GameInitialize.HandlerLoader?.warMachineHandler?.warMachineData?.formationList;
            if (data == null) { Finish(false, "Saved, but the squad could not be read back to check it."); return; }

            var problems = new List<string>();
            foreach (var p in plan)
            {
                WarMachineSpotData spot = null;
                for (var i = 0; i < data.Count; i++)
                    if (data[i] != null && IsMachine(data[i].code, p.Code)) { spot = data[i]; break; }
                if (spot == null) { problems.Add($"{p.Code} is not in the squad"); continue; }
                var crew = new HashSet<int>();
                for (var i = 0; spot.warMachineHeroCodes != null && i < spot.warMachineHeroCodes.Count; i++) crew.Add(spot.warMachineHeroCodes[i]);
                var missing = p.Crew.Count(id => !crew.Contains(id));
                if (missing > 0) problems.Add($"{p.Code} is missing {missing} crew");
            }

            if (problems.Count == 0) { Finish(true, "Squad applied. The game now runs your planned machines and crews."); return; }
            var saved = new List<string>();
            for (var i = 0; i < data.Count; i++)
                if (data[i] != null && !string.IsNullOrEmpty(data[i].code)) saved.Add(NameOf(data[i].code));
            Finish(false, "Applied with differences: " + string.Join("; ", problems) +
                          $". Saved squad: [{string.Join(", ", saved)}]. On screen before saving: [{onScreen}].");
        }
        catch (Exception e)
        {
            Finish(false, $"Saved, but checking it failed: {e.Message}");
        }
    }

    private static void Finish(bool ok, string message)
    {
        Ok = ok;
        Status = message;
        Busy = false;
        if (ok) Logger.Info($"[Planner] {message}");
        else Logger.Warning($"[Planner] {message}");
    }

    private static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.Ordinal);

    private static string Norm(string s) =>
        s == null ? "" : new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
