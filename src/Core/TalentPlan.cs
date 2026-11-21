using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Firebot.Core;

/// <summary>
///     A staged talent plan: an ordered list of "Name#copy:level" steps. The tree repeats names across tiers
///     (Battle cry, Alchemy, Leadership...), so "#2" means the second copy of that talent counting from the top
///     tier. Each step raises that node to the given level (or its cap) before the next step starts.
/// </summary>
internal static class TalentPlan
{
    public readonly record struct Step(string Name, int Copy, int Target);

    // Default staged order (character-level ranges 1-415): attributes and Leadership first, then Trainer skills, Meteorite hunter, Raining gold and the rest.
    public const string Guide =
        "All main attributes#1:9,Leadership#1:1,Leader - Auto Abilities#1:1,All main attributes#1:13," +
        "Trainer skills#1:5,Auto attacks#1:1,Trainer skills#1:13,Critical damage#1:1," +
        "Meteorite hunter#1:20,Trainer skills#1:20,Powerless boss#1:1,Expose weakness#1:1," +
        "Ancient knowledge#1:1,Raining gold#1:5,Coworkers#1:5,Trainer skills#1:25,Librarian#1:16," +
        "Expeditioner#1:6,Twin dragons#1:1,Critical chance#2:1,Battle cry#1:15,Expeditioner#1:20," +
        "Twin dragons#1:10,Ancient knowledge#1:20,Critical damage#2:1,Powerless boss#2:1,Alchemy#1:25," +
        "Expose weakness#2:1,All main attributes#1:25,Leadership#1:25,All main attributes#2:25," +
        "Leadership#2:25,Team bonus#1:25,Twin dragons#2:15,Team bonus#2:25,Critical damage#1:11," +
        "Librarian#2:25,Alchemy#2:25,Battle cry#2:15";

    private static readonly Regex StepPattern =
        new(@"^(?<name>.+?)\s*(?:#\s*(?<copy>\d+))?\s*:\s*(?<level>\d+)$", RegexOptions.CultureInvariant);

    private static readonly Regex NodeNumber = new(@"\((\d+)\)\s*$", RegexOptions.CultureInvariant);

    /// <summary>Parses the plan. Returns false (and no steps) when any entry is malformed.</summary>
    public static bool TryParse(string plan, out List<Step> steps, out string error)
    {
        steps = new List<Step>();
        error = null;
        foreach (var raw in (plan ?? "").Split(','))
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;
            var m = StepPattern.Match(entry);
            if (!m.Success ||
                !int.TryParse(m.Groups["level"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var level) ||
                level <= 0)
            {
                error = $"'{entry}' is not Name#copy:level";
                steps.Clear();
                return false;
            }
            var copy = 1;
            if (m.Groups["copy"].Success &&
                (!int.TryParse(m.Groups["copy"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out copy) ||
                 copy <= 0))
            {
                error = $"'{entry}' has a bad copy number";
                steps.Clear();
                return false;
            }
            steps.Add(new Step(m.Groups["name"].Value.Trim(), copy, level));
        }
        return true;
    }

    /// <summary>Tree order of a node from its Unity name, "talentInteraction (12)" → 12; plain name → 0.</summary>
    public static int NodeOrder(string nodeName, int siblingIndex)
    {
        var m = NodeNumber.Match(nodeName ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : siblingIndex;
    }

    /// <summary>
    ///     Copies of one talent, in tree order, must unlock top-down: a later copy can never be open while an
    ///     earlier one is still locked. If that is violated the tree order was misread, so the plan must hold.
    /// </summary>
    public static bool CopiesConsistent(IReadOnlyList<bool> unlockedInOrder)
    {
        var sawLocked = false;
        foreach (var unlocked in unlockedInOrder)
        {
            if (!unlocked) sawLocked = true;
            else if (sawLocked) return false;
        }
        return true;
    }

    /// <summary>Level a step actually asks for: its target, capped at the node's max.</summary>
    public static int Goal(Step step, int cap) => cap > 0 ? Math.Min(step.Target, cap) : step.Target;

    /// <summary>Names match ignoring case, spaces and punctuation ("Party - Auto Abilities" = "Party auto-abilities").</summary>
    public static bool SameName(string a, string b) => Key(a).Length > 0 && Key(a) == Key(b);

    private static string Key(string s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static HashSet<string> Words(string s) =>
        new(Regex.Split((s ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length > 1));

    /// <summary>
    ///     The tree's own spelling of a plan name: an exact match ignoring punctuation, else the single tree name
    ///     whose words contain the plan's words (or the reverse). Null when nothing, or more than one, fits.
    /// </summary>
    public static string Resolve(string planName, IEnumerable<string> treeNames)
    {
        var names = treeNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        var exact = names.FirstOrDefault(n => SameName(n, planName));
        if (exact != null) return exact;
        var want = Words(planName);
        if (want.Count == 0) return null;
        var loose = names.Where(n =>
        {
            var have = Words(n);
            return have.Count > 0 && (want.IsSubsetOf(have) || have.IsSubsetOf(want));
        }).Select(Key).Distinct().ToList();
        return loose.Count == 1 ? names.First(n => Key(n) == loose[0]) : null;
    }

    public static string Describe(Step s) => $"{s.Name} #{s.Copy} → {s.Target}";

    public static IEnumerable<string> Names(IEnumerable<Step> steps) =>
        steps.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase);
}
