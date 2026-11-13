using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;

namespace Firebot.Core;

/// <summary>
///     Guide-backed default orders for every priority list, kept in one place so the tasks, the panel's
///     Recommended chip and the one-time adoption all agree.
///     <para>
///         Sources: Aurora's Steam "Complete Guide" (id 2788843533), the Steam War Machine guide (id 3083873998),
///         the Firestone Fandom wiki (Talents, Oracle, Forbidden Knowledge) and firestone-idle-rpg.de/wiki.
///         Locked entries (Fate, Battle cry) can sit first: every buyer skips what it cannot click yet.
///     </para>
/// </summary>
internal static class RecommendedOrders
{
    public const string Talents =
        "Battle cry,Fate,Leader - Auto Abilities,Party - Auto Abilities,All main attributes,Raining gold," +
        "Guardian power,Leadership,Team bonus,Attribute damage,Librarian,Alchemy,Meteorite hunter,Trainer skills," +
        "Expeditioner,Twin dragons,Coworkers,Attribute health,Attribute armor,Critical chance,Critical damage," +
        "Attack speed,Dodge,Fist fight,Precision,Magic spells,Mana heroes,Energy heroes,Rage heroes," +
        "Damage specialization,Tank specialization,Healer specialization,Weaklings,Expose weakness," +
        "Powerless enemy,Powerless boss,Ancient knowledge";

    public const string MeteoriteResearch =
        "All main attributes,Firestone finder,Raining gold,Attribute damage,Hero level up cost,Attribute health," +
        "Attribute armor,Powerless enemy,Powerless boss";

    // Firestone Finder, Raining gold, then the attribute line; utility nodes last.
    public const string FirestoneResearch = "13,7,1,5,2,3,4,6,14,15,16,8,9,12,10,11";

    public const string PersonalTree =
        "Prestigious,Firestone effect,Battle cry,All main attributes,Raining gold,Attribute damage," +
        "Hero level up cost,Attribute health,Attribute armor,Guardian,Fist fight,Precision,Magic spells";

    public const string ForbiddenKnowledge =
        "All main attributes,Firestone Finder,Guardian power,Raining gold,Attribute damage,Leadership,Team bonus," +
        "Heroes' damage,Attribute health,Attribute armor,Fist fight,Precision,Magic spells,Damage specialization," +
        "Tank specialization,Healer specialization,Rage Heroes,Mana Heroes,Energy Heroes";

    public const string Oracle =
        "Fate,Guardian Power,Prestigious,Raining gold,Damage Specialization,Tank Specialization," +
        "Healer Specialization,Melee Heroes,Ranged Heroes,Spellcaster Heroes,Mana Heroes,Rage Heroes,Energy Heroes";

    public const string ExoticMerchant =
        "All main attributes,Prestigious,Firestone effect,Firestone Finder,Raining gold,Heroes' damage,Leadership," +
        "Team bonus,Guardian power,Attribute health,Attribute armor,Damage specialization,Tank specialization," +
        "Healer specialization,Fist fight,Precision,Magic spells,Mana Heroes,Energy Heroes,Rage Heroes," +
        "Skip stage,Skip wave";

    // Every item stays listed: unlisted event items are never bought.
    public const string EventExchange =
        "Golden key,Meteorite,Legendary chest,Strange dust,Exotic coin,Pickaxe,Soul ember,Twilight hourglass";

    public const string Blueprints = "damage,health,armor";

    /// <summary>Lists switched to the guide order once on update; key is "category/entry".</summary>
    public static readonly IReadOnlyDictionary<string, string> Adopted = new Dictionary<string, string>
    {
        ["talents/priority"] = Talents,
        ["meteorite_research/priority"] = MeteoriteResearch,
        ["firestone_research/research_priority"] = FirestoneResearch,
        ["personal_tree/priority"] = PersonalTree,
        ["forbidden_knowledge/priority"] = ForbiddenKnowledge,
        ["oracle_blessings/priority"] = Oracle,
        ["exotic_merchant/priority"] = ExoticMerchant,
        ["event_exchange/priority"] = EventExchange
    };

    /// <summary>
    ///     Moves an existing player onto the guide order once per list. Later edits are left alone, and
    ///     the panel's Recommended chip restores the order at any time.
    /// </summary>
    public static void Adopt(MelonPreferences_Category category, MelonPreferences_Entry<string> entry)
    {
        if (category == null || entry == null) return;
        var key = $"{category.Identifier}/{entry.Identifier}";
        if (!Adopted.TryGetValue(key, out var order)) return;
        if (!BotSettings.TryMarkGuideOrderAdopted(key)) return;
        if (!Same(entry.Value, order))
        {
            entry.Value = order;
            category.SaveToFile();
            Logger.Info($"[Settings] {key} now follows the guide order. Edit it in F1, or tap Recommended to restore it.");
        }
        BotSettings.Category?.SaveToFile();
    }

    /// <summary>Panel chip: clear empties the list, recommended restores the shipped order, anything else toggles.</summary>
    public static void ApplyChip(List<string> selected, string id, string clearId, string recommendedId, string recommended)
    {
        if (id == clearId)
        {
            selected.Clear();
            return;
        }
        if (id == recommendedId)
        {
            selected.Clear();
            foreach (var name in Split(recommended))
                if (!selected.Contains(name)) selected.Add(name);
            return;
        }
        if (!selected.Remove(id)) selected.Add(id);
    }

    public static bool Same(string a, string b) => Split(a).SequenceEqual(Split(b), StringComparer.OrdinalIgnoreCase);

    public static List<string> Split(string value) =>
        (value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
}
