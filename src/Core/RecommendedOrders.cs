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
        "Leader - Auto Abilities,Auto attacks,Librarian,Alchemy,Twin dragons,Battle cry,Fate," +
        "Expeditioner,Trainer Skills,Coworkers,Meteorite hunter,Raining gold,All main attributes," +
        "Team bonus,Fist fight,Precision,Magic spells,Leadership,Attack speed,Damage specialization," +
        "Tank specialization,Healer specialization,Mana heroes,Energy heroes,Rage heroes," +
        "Critical chance,Critical damage,Weaklings,Powerless enemy,Powerless boss,Dodge,Expose Weakness," +
        "Ancient Knowledge,Guardian power";

    public const string MeteoriteResearch =
        "Meteorite research,Raining gold,Firestone effect,Firestone Finder,All main attributes,Attribute damage," +
        "Hero level up cost,Medal of honor,Attribute health,Attribute armor,Damage specialization," +
        "Tank specialization,Healer specialization,Expose Weakness,Weaklings,Skip wave,Skip stage," +
        "Scroll of damage effect,Scroll of speed effect,Scroll of health effect";

    // Aurora: Raining gold is the best upgrade, then the time cuts (Trainer skills, Expeditioner) and attribute
    // damage. Firestone Finder is weak in this tree (it shines in the Exotic Merchant and meteorite tree 3), health
    // and armor go last together, and critical loot, Weaklings and Medal of honor are poor.
    public const string FirestoneResearch = "7,14,16,1,5,4,6,15,13,2,3,11,12,10,8,9";

    public const string PersonalTree =
        "Battle cry,Miner,Firestone Finder,Raining gold,Attribute damage,All main attributes," +
        "Firestone effect,Energy heroes,Mana heroes,Rage heroes,Fist fight,Precision,Magic spells," +
        "Hero level up cost,Guardian power,Damage specialization,Tank specialization," +
        "Healer specialization,Attribute health,Attribute armor";

    public const string ForbiddenKnowledge =
        "All main attributes,Firestone Finder,Guardian power,Raining gold,Attribute damage,Leadership,Team bonus," +
        "Heroes' damage,Attribute health,Attribute armor,Fist fight,Precision,Magic spells,Damage specialization," +
        "Tank specialization,Healer specialization,Rage Heroes,Mana Heroes,Energy Heroes";

    public const string Oracle =
        "Fate,Firestone Finder,Raining gold,Guardian power,Damage specialization,Tank specialization," +
        "Healer specialization,Fist fight,Precision,Magic spells,Mana heroes,Rage heroes,Energy heroes";

    public const string ExoticMerchant =
        "Firestone Finder,Raining gold,Firestone effect,Prestigious,All main attributes,Heroes' damage," +
        "Leadership,Team bonus,Guardian power,Damage specialization,Tank specialization," +
        "Healer specialization,Fist fight,Precision,Magic spells,Mana Heroes,Energy Heroes,Rage Heroes," +
        "Attribute health,Attribute armor,Skip stage,Skip wave";

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
    ///     The orders 0.7.62 and earlier shipped. A list still exactly equal to one of these was never edited by
    ///     the player, so it moves to the new guide order once; any edited list is left alone.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> PreviousDefaults = new Dictionary<string, string[]>
    {
        ["talents/priority"] =
            new[] { "Battle cry,Fate,Leader - Auto Abilities,Party - Auto Abilities,All main attributes,Raining gold,Guardian power,Leadership,Team bonus,Attribute damage,Librarian,Alchemy,Meteorite hunter,Trainer skills,Expeditioner,Twin dragons,Coworkers,Attribute health,Attribute armor,Critical chance,Critical damage,Attack speed,Dodge,Fist fight,Precision,Magic spells,Mana heroes,Energy heroes,Rage heroes,Damage specialization,Tank specialization,Healer specialization,Weaklings,Expose weakness,Powerless enemy,Powerless boss,Ancient knowledge" },
        ["meteorite_research/priority"] =
            new[] { "All main attributes,Firestone finder,Raining gold,Attribute damage,Hero level up cost,Attribute health,Attribute armor,Powerless enemy,Powerless boss" },
        ["firestone_research/research_priority"] =
            new[] { "13,7,1,5,2,3,4,6,14,15,16,8,9,12,10,11", "14,16,13,7,1,5,2,3,4,6,15,12,10,11,8,9" },
        ["personal_tree/priority"] =
            new[] { "Prestigious,Firestone effect,Battle cry,All main attributes,Raining gold,Attribute damage,Hero level up cost,Attribute health,Attribute armor,Guardian,Fist fight,Precision,Magic spells" },
        ["oracle_blessings/priority"] =
            new[] { "Fate,Guardian Power,Prestigious,Raining gold,Damage Specialization,Tank Specialization,Healer Specialization,Melee Heroes,Ranged Heroes,Spellcaster Heroes,Mana Heroes,Rage Heroes,Energy Heroes" },
        ["exotic_merchant/priority"] =
            new[] { "All main attributes,Prestigious,Firestone effect,Firestone Finder,Raining gold,Heroes' damage,Leadership,Team bonus,Guardian power,Attribute health,Attribute armor,Damage specialization,Tank specialization,Healer specialization,Fist fight,Precision,Magic spells,Mana Heroes,Energy Heroes,Rage Heroes,Skip stage,Skip wave" }
    };

    /// <summary>Bumped when the shipped orders change, so untouched lists move to the new order once.</summary>
    private const string OrderRevision = "aurora-2026-11b";

    /// <summary>
    ///     Moves an existing player onto the guide order once per list. Later edits are left alone, and
    ///     the panel's Recommended chip restores the order at any time.
    /// </summary>
    public static void Adopt(MelonPreferences_Category category, MelonPreferences_Entry<string> entry)
    {
        if (category == null || entry == null) return;
        var key = $"{category.Identifier}/{entry.Identifier}";
        if (!Adopted.TryGetValue(key, out var order)) return;
        // First adoption (fresh install or a list never adopted): switch unconditionally, as before.
        // Revision adoption: only when the list still matches what an older version shipped.
        var first = BotSettings.TryMarkGuideOrderAdopted(key);
        if (!first)
        {
            if (!BotSettings.TryMarkGuideOrderAdopted(key + "@" + OrderRevision)) return;
            if (!PreviousDefaults.TryGetValue(key, out var previous) || !previous.Any(old => Same(entry.Value, old))) return;
        }
        else BotSettings.TryMarkGuideOrderAdopted(key + "@" + OrderRevision);
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
