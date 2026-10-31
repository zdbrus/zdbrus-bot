using System;
using System.Collections;
using System.Linq;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Features.Town.Library.FirestoneResearch;
using Firebot.GameModel.Shared;
using Firebot.Infrastructure;
using MelonLoader;

namespace Firebot.Behaviors.Town;

public class FirestoneResearchTask : BotTask
{
    private enum Attempt { Unavailable, Started, Blocked }
    private MelonPreferences_Entry<string> _researchPriority;
    protected override string NotificationPath => Paths.BattleLoc.NotificationsLoc.FirestoneResearchBtn;
    protected override bool UseScheduledTime => true;

    private const string Library = P.Menus + "/Library";
    private const string Research = Library + "/submenus/firestoneResearch";

    public override IEnumerator Execute()
    {
        if (!Ui.Visible(Research))
        {
            if (Ui.Visible(NotificationPath))
            {
                yield return Notifications.FirestoneResearch;
                yield return Ui.WaitFor(Research, 2f);
            }

            if (!Ui.Visible(Research))
                yield return Ui.OpenTownSubmenu("library", Research, Library + "/submenuButtons/firestoneResearch");
        }

        if (!Ui.Visible(Research))
        {
            Logger.Warning("[Firestone Research] Could not open the research screen; retrying in one hour.");
            NextRunTime = DateTime.Now.AddHours(1);
            if (Ui.Visible(Library)) yield return Ui.Click(Library + "/closeButton");
            yield break;
        }

        var panel = new ResearchPanel();
        yield return panel.Claim();

        if (!ResearchPanel.HasEmptySlot)
        {
            Debug("[INFO] No empty slots. Scheduling next run.");
            NextRunTime = panel.NextRunTime();
            yield break;
        }

        var blocked = false;
        yield return RunSelection(value => blocked = value);
        NextRunTime = panel.NextRunTime();
        if (blocked && NextRunTime > DateTime.Now.AddMinutes(15))
            NextRunTime = DateTime.Now.AddMinutes(15);
    }

    private IEnumerator SelectWithPreview(Node node, int? index, Action<Attempt> result)
    {
        if (Preview.IsVisible)
        {
            var closed = false;
            yield return Preview.Close(value => closed = value);
            if (!closed)
            {
                Logger.Warning("[Firestone Research] Existing preview could not be closed; no research was started.");
                result(Attempt.Blocked);
                yield break;
            }
        }

        var selection = ResearchSelectionResult.Failed;
        if (index.HasValue)
            yield return node.Select(index.Value, value => selection = value);
        else
            yield return node.SelectAny(value => selection = value);

        var attempt = selection == ResearchSelectionResult.Unavailable ? Attempt.Unavailable : Attempt.Blocked;
        if (selection == ResearchSelectionResult.Selected && Preview.IsUnlocked && !Preview.IsMaxed)
        {
            var started = false;
            yield return Preview.Start(value => started = value);
            attempt = started ? Attempt.Started : Attempt.Blocked;
        }
        else if (selection == ResearchSelectionResult.Selected)
            attempt = Attempt.Unavailable;

        if (Preview.IsVisible)
        {
            var closed = false;
            yield return Preview.Close(value => closed = value);
            if (!closed)
            {
                Logger.Warning("[Firestone Research] Preview did not close; stopping selection.");
                attempt = Attempt.Blocked;
            }
        }
        result(attempt);
    }

    private IEnumerator RunSelection(Action<bool> blocked)
    {
        var priority = GetResearchPriority();
        var node = new Node();

        Debug(priority is { Length: > 0 }
            ? $"[INFO] Priority list loaded: {string.Join(",", priority)}"
            : "[INFO] No priority list. Using fallback selection.");

        while (ResearchPanel.HasEmptySlot)
        {
            var started = false;

            if (priority is { Length: > 0 })
                foreach (var priorityIndex in priority)
                    if (int.TryParse(priorityIndex, out var index))
                    {
                        var attempt = Attempt.Unavailable;
                        yield return SelectWithPreview(node, index, value => attempt = value);
                        if (attempt == Attempt.Blocked)
                        {
                            blocked(true);
                            yield break;
                        }
                        if (attempt != Attempt.Started) continue;
                        started = true;
                        break;
                    }

            if (started || !ResearchPanel.HasEmptySlot) continue;
            var fallback = Attempt.Unavailable;
            yield return SelectWithPreview(node, null, value => fallback = value);
            if (fallback == Attempt.Blocked)
            {
                blocked(true);
                yield break;
            }
            if (fallback != Attempt.Started) break;
        }
        blocked(false);
    }

    protected override void OnConfigure(MelonPreferences_Category category)
    {
        if (_researchPriority != null) return;

        _researchPriority = category.CreateEntry(
            "research_priority",
            "",
            "Research Priority",
            $"FIRESTONE RESEARCH TALENT TREE PRIORITY CONFIGURATION. " +
            $"\nThis setting controls which talents are researched first based on their tree position. " +
            $"\nTALENT IDs ARE ASSIGNED BY INDEX (ordered top to bottom, left to right within each tree screen). " +
            $"\nValid IDs for user input range from 1 to 16. " +
            $"\nYou may specify any combination of IDs from 1 to 16, in any order you prefer. The bot will follow the exact order you provide. " +
            $"\nTREE I EXAMPLE - ID 1=Attribute damage, ID 2=Attribute health, ID 3=Attribute armor, ID 4=Fist fight, ID 5=Guardian power, ID 6=Projectiles, " +
            $"\nID 7=Raining gold, ID 8=Critical loot Bonus, ID 9=Critical loot Chance, ID 10=Weaklings, ID 11=Expose Weakness, " +
            $"\nID 12=Medal of honor, ID 13=Firestone Finder, ID 14=Trainer Skills, ID 15=Skip wave, ID 16=Expeditioner. " +
            $"\nHOW TO USE: Enter comma-separated IDs in priority order (integers between 1-16). The bot will research talents in the exact sequence provided. " +
            $"\nIf a priority talent is unavailable (locked/completed), the bot will try the next priority. " +
            $"\nIf all priorities are unavailable or if this field is empty, the bot will select any available talent automatically. " +
            $"\nEXAMPLES: '2,1,4' = Research Attribute health first, then Attribute damage, then Fist fight. " +
            $"\n'7,8,9' = Research Raining gold first, then Critical loot Bonus, then Critical loot Chance. " +
            $"\n'13' = Only research Firestone Finder, fallback to any available if completed. " +
            $"\n'5,12,1,16,3,8,10,2,14,7,4,15,6,13,9,11' = Example using all 16 IDs in a random order, each ID only once. " +
            $"\nDefault: empty (no priority, bot selects any available talent)"
        );
    }

    private string[] GetResearchPriority()
    {
        if (_researchPriority == null)
        {
            Debug("[INFO] Missing research_priority entry. No priority set.");
            return null;
        }

        var value = _researchPriority.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            Debug("[INFO] Empty research_priority value. No priority set.");
            return null;
        }

        var priorities = value.Split(',')
            .Select(x => x.Trim())
            .Where(x => int.TryParse(x, out var id) && id >= 1 && id <= 16)
            .Select(x => int.Parse(x).ToString())
            .ToArray();

        if (priorities.Length != 0) return priorities;
        
        Debug($"[FAILED] Invalid research_priority '{value}'. All values must be integers between 1-16. No priority set.");
        return null;

    }
}