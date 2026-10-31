using System;
using System.Collections.Generic;
using System.Reflection;
using Firebot.Core;
using MelonLoader;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.UI.Config;

internal sealed class ConfigGroup
{
    public ConfigGroup(MelonPreferences_Category category, List<ConfigEntry> entries)
    {
        Category = category;
        Entries = entries;
        Title = string.IsNullOrWhiteSpace(category.DisplayName) ? category.Identifier : category.DisplayName;
    }

    public MelonPreferences_Category Category { get; }
    public string Title { get; }
    public List<ConfigEntry> Entries { get; }
}

/// <summary>
///     Reads the global MelonPreferences registry and returns only what belongs to our .cfg.
///     Since every task passes <c>BotSettings.ConfigPath</c> to SetFilePath, filtering by file picks
///     up everything (firebot_settings, autoskill, autoupgrade and one section per task) without the
///     UI having to know about any of them.
/// </summary>
internal static class ConfigRegistry
{
    private const float SaveDelaySeconds = 0.4f;

    private static readonly HashSet<MelonPreferences_Category> DirtyCategories = new();
    private static readonly HashSet<string> ReportedUnwritable = new();

    /// <summary>
    ///     MelonLoader keeps the category → file association in an internal field and exposes no
    ///     public API to query it (not Category.File, not File.FilePath, not
    ///     GetPrefFileFromFilePath). Since there is no way to filter without it, we reflect that one
    ///     field and compare references: same File instance = same .cfg.
    /// </summary>
    private static readonly FieldInfo CategoryFileField = typeof(MelonPreferences_Category).GetField(
        "File", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static bool _filterFailureLogged;

    private static bool _hasPendingSave;
    private static float _lastChangeAt;

    /// <summary>Keys of the entries seen in the current read, used to spot declarations that match nothing.</summary>
    private static readonly HashSet<string> SeenKeys = new();

    private static bool _unmatchedDeclarationsLogged;

    /// <summary>
    ///     Ranges declared for the entries the UI has to draw as a slider.
    ///     The firebot_settings ones come from the public BotSettings constants (the same source as
    ///     the Clamp). The two task ones exist only in the description text ("Use 0-3", "up to 30"),
    ///     so they are repeated here on purpose — BotTask exposes no range metadata.
    /// </summary>
    private static readonly Dictionary<string, NumberRange> DeclaredRanges = new()
    {
        ["firebot_settings/start_bot_delay"] =
            new NumberRange(BotSettings.StartBotDelayMin, BotSettings.StartBotDelayMax, false),
        ["firebot_settings/scan_interval"] =
            new NumberRange(BotSettings.ScanIntervalMin, BotSettings.ScanIntervalMax, false),
        ["firebot_settings/interaction_delay"] =
            new NumberRange(BotSettings.InteractionDelayMin, BotSettings.InteractionDelayMax, false),
        ["firebot_settings/max_task_runtime"] =
            new NumberRange(BotSettings.MaxTaskRuntimeMin, BotSettings.MaxTaskRuntimeMax, false),
        ["firebot_settings/free_speedup_seconds"] =
            new NumberRange(BotSettings.FreeSpeedupSecondsMin, BotSettings.FreeSpeedupSecondsMax, false),
        ["magic_quarters/guardian_index"] = new NumberRange(0f, 3f, true),
        ["free_pickaxes/pickaxe_claim_threshold"] = new NumberRange(1f, 30f, true),
        ["exotic_merchant/sell_mode"] = new NumberRange(0f, 3f, true),
        ["arcane_crystal/keep_pickaxes"] = new NumberRange(0f, 500f, true),
        ["arena/max_opponent_power_percent"] = new NumberRange(50f, 150f, true),
        ["personal_tree/keep_tokens"] = new NumberRange(0f, 5000f, true),
        ["meteorite_research/keep_meteorites"] = new NumberRange(0f, 100000f, true),
        ["temple_prestige/min_gain_percent"] = new NumberRange(0f, 100000f, true),
        ["temple_prestige/stall_hours"] = new NumberRange(1f, 48f, true),
        ["temple_prestige/min_hours_played"] = new NumberRange(0f, 72f, true),
        ["temple_prestige/efficiency_percent"] = new NumberRange(0f, 200f, true),
        ["event_exchange/keep_currency"] = new NumberRange(0f, 100000f, true),
        ["auto_upgrade/every_seconds"] = new NumberRange(10f, 600f, true),
        ["boss_retry/every_minutes"] = new NumberRange(1f, 60f, true)
    };

    /// <summary>
    ///     Single choice: the stored value is exactly one of the ids.
    ///     Works for a string entry (the whole id, e.g. 'asc') and for a numeric one (the id as a
    ///     number).
    /// </summary>
    private static readonly Dictionary<string, ConfigChoice[]> SingleChoices = new()
    {
        ["auto_upgrade/upgrade_mode"] = new[]
        {
            new ConfigChoice("milestone", "Next milestone"),
            new ConfigChoice("max", "Max"),
            new ConfigChoice("x100", "x100"),
            new ConfigChoice("x25", "x25"),
            new ConfigChoice("x10", "x10"),
            new ConfigChoice("x1", "x1")
        },
        ["auto_upgrade/priority_mode"] = new[]
        {
            new ConfigChoice("balanced", "Balance"),
            new ConfigChoice("focus", "Focus first")
        },
        ["chaos_rift/holy_priority_mode"] = new[]
        {
            new ConfigChoice("balanced", "Balance"),
            new ConfigChoice("focus", "Focus first")
        },
        ["magic_quarters/guardian_index"] = new[]
        {
            new ConfigChoice("0", "Vermilion"),
            new ConfigChoice("1", "Grace"),
            new ConfigChoice("2", "Ankaa"),
            new ConfigChoice("3", "Azhar")
        },
        ["map_missions/mission_time_order"] = new[]
        {
            new ConfigChoice("asc", "Shorter first"),
            new ConfigChoice("desc", "Longer first")
        },
        ["exotic_merchant/sell_mode"] = new[]
        {
            new ConfigChoice("0", "Nothing"),
            new ConfigChoice("1", "Scrolls"),
            new ConfigChoice("2", "All but gold"),
            new ConfigChoice("3", "Everything")
        },
        ["war_machine_upgrade/blueprints"] = new[]
        {
            new ConfigChoice("none", "None"),
            new ConfigChoice("damage", "Damage"),
            new ConfigChoice("health", "Health"),
            new ConfigChoice("armor", "Armor"),
            new ConfigChoice("all", "All")
        },
    };

    /// <summary>
    ///     Multiple choice: any subset of the ids, stored comma-separated.
    ///     Only valid for a string entry — a number cannot hold a list.
    /// </summary>
    private static readonly Dictionary<string, ConfigChoice[]> MultiChoices = new()
    {
        ["exotic_merchant/priority"] = new[]
        {
            new ConfigChoice("Prestigious", "Prestigious"),
            new ConfigChoice("Firestone Finder", "Firestone Finder"),
            new ConfigChoice("Raining gold", "Raining gold"),
            new ConfigChoice("Heroes' damage", "Heroes' damage"),
            new ConfigChoice("Team bonus", "Team bonus"),
            new ConfigChoice("All main attributes", "All main attributes"),
            new ConfigChoice("Firestone effect", "Firestone effect"),
            new ConfigChoice("Damage specialization", "Damage specialization"),
            new ConfigChoice("Leadership", "Leadership"),
            new ConfigChoice("Guardian power", "Guardian power"),
            new ConfigChoice("Energy Heroes", "Energy Heroes"),
            new ConfigChoice("Mana Heroes", "Mana Heroes"),
            new ConfigChoice("Rage Heroes", "Rage Heroes"),
            new ConfigChoice("Fist fight", "Fist fight"),
            new ConfigChoice("Magic spells", "Magic spells"),
            new ConfigChoice("Precision", "Precision"),
            new ConfigChoice("Skip stage", "Skip stage"),
            new ConfigChoice("Skip wave", "Skip wave"),
            new ConfigChoice("Healer specialization", "Healer specialization"),
            new ConfigChoice("Tank specialization", "Tank specialization"),
            new ConfigChoice("Attribute health", "Attribute health"),
            new ConfigChoice("Attribute armor", "Attribute armor")
        },
        ["oracle_blessings/priority"] = new[]
        {
            new ConfigChoice("Fate", "Fate"),
            new ConfigChoice("Prestigious", "Prestigious"),
            new ConfigChoice("Raining gold", "Raining gold"),
            new ConfigChoice("Damage Specialization", "Damage specialization"),
            new ConfigChoice("Guardian Power", "Guardian power"),
            new ConfigChoice("Mana Heroes", "Mana heroes"),
            new ConfigChoice("Energy Heroes", "Energy heroes"),
            new ConfigChoice("Rage Heroes", "Rage heroes"),
            new ConfigChoice("Melee Heroes", "Melee heroes"),
            new ConfigChoice("Ranged Heroes", "Ranged heroes"),
            new ConfigChoice("Spellcaster Heroes", "Spellcaster heroes"),
            new ConfigChoice("Tank Specialization", "Tank specialization"),
            new ConfigChoice("Healer Specialization", "Healer specialization")
        },
        ["firestone_research/research_priority"] = new[]
        {
            new ConfigChoice("1", "1: Attribute damage"),
            new ConfigChoice("2", "2: Attribute health"),
            new ConfigChoice("3", "3: Attribute armor"),
            new ConfigChoice("4", "4: Fist fight"),
            new ConfigChoice("5", "5: Guardian power"),
            new ConfigChoice("6", "6: Projectiles"),
            new ConfigChoice("7", "7: Raining gold"),
            new ConfigChoice("8", "8: Critical loot bonus"),
            new ConfigChoice("9", "9: Critical loot chance"),
            new ConfigChoice("10", "10: Weaklings"),
            new ConfigChoice("11", "11: Expose weakness"),
            new ConfigChoice("12", "12: Medal of honor"),
            new ConfigChoice("13", "13: Firestone Finder"),
            new ConfigChoice("14", "14: Trainer skills"),
            new ConfigChoice("15", "15: Skip wave"),
            new ConfigChoice("16", "16: Expeditioner")
        },
        ["war_machine_upgrade/war_machine"] = new[]
        {
            new ConfigChoice("squad", "Active squad"),
            new ConfigChoice("4", "Aegis"),
            new ConfigChoice("11", "Cloudfist"),
            new ConfigChoice("6", "Curator"),
            new ConfigChoice("10", "Earthshatterer"),
            new ConfigChoice("3", "Firecracker"),
            new ConfigChoice("1", "Fortress"),
            new ConfigChoice("0", "Goliath"),
            new ConfigChoice("8", "Harvester"),
            new ConfigChoice("12", "Hunter"),
            new ConfigChoice("5", "Judgement"),
            new ConfigChoice("7", "Sentinel"),
            new ConfigChoice("9", "Talos"),
            new ConfigChoice("2", "Thunderclap")
        },
        ["war_machine_upgrade/blueprint_priority"] = new[]
        {
            new ConfigChoice("damage", "Damage"),
            new ConfigChoice("health", "Health"),
            new ConfigChoice("armor", "Armor")
        },
        ["chaos_rift/holy_guardians"] = new[]
        {
            new ConfigChoice("Vermilion", "Vermilion"),
            new ConfigChoice("Grace", "Grace"),
            new ConfigChoice("Ankaa", "Ankaa"),
            new ConfigChoice("Azhar", "Azhar")
        },
        ["event_exchange/priority"] = new[]
        {
            new ConfigChoice("Pickaxe", "Pickaxe"),
            new ConfigChoice("Legendary chest", "Legendary chest"),
            new ConfigChoice("Meteorite", "Meteorite"),
            new ConfigChoice("Golden key", "Golden key"),
            new ConfigChoice("Strange dust", "Strange dust"),
            new ConfigChoice("Twilight hourglass", "Twilight hourglass"),
            new ConfigChoice("Soul ember", "Soul ember"),
            new ConfigChoice("Exotic coin", "Exotic coin")
        },
        ["personal_tree/priority"] = new[]
        {
            new ConfigChoice("Raining gold", "Raining gold"),
            new ConfigChoice("Battle cry", "Battle cry"),
            new ConfigChoice("Prestigious", "Prestigious"),
            new ConfigChoice("All main attributes", "All main attributes"),
            new ConfigChoice("Attribute damage", "Attribute damage"),
            new ConfigChoice("Firestone effect", "Firestone effect"),
            new ConfigChoice("Hero level up cost", "Hero level up cost"),
            new ConfigChoice("Attribute health", "Attribute health"),
            new ConfigChoice("Attribute armor", "Attribute armor"),
            new ConfigChoice("Guardian", "Guardian"),
            new ConfigChoice("Precision", "Precision"),
            new ConfigChoice("Fist fight", "Fist fight"),
            new ConfigChoice("Magic spells", "Magic spells")
        },
        ["forbidden_knowledge/priority"] = new[]
        {
            new ConfigChoice("Firestone Finder", "Firestone Finder"),
            new ConfigChoice("All main attributes", "All main attributes"),
            new ConfigChoice("Attribute damage", "Attribute damage"),
            new ConfigChoice("Raining gold", "Raining gold"),
            new ConfigChoice("Guardian power", "Guardian power"),
            new ConfigChoice("Attribute health", "Attribute health"),
            new ConfigChoice("Attribute armor", "Attribute armor"),
            new ConfigChoice("Heroes' damage", "Heroes' damage"),
            new ConfigChoice("Leadership", "Leadership"),
            new ConfigChoice("Team bonus", "Team bonus"),
            new ConfigChoice("Rage Heroes", "Rage Heroes"),
            new ConfigChoice("Mana Heroes", "Mana Heroes"),
            new ConfigChoice("Energy Heroes", "Energy Heroes"),
            new ConfigChoice("Fist fight", "Fist fight"),
            new ConfigChoice("Precision", "Precision"),
            new ConfigChoice("Magic spells", "Magic spells"),
            new ConfigChoice("Damage specialization", "Damage specialization"),
            new ConfigChoice("Healer specialization", "Healer specialization"),
            new ConfigChoice("Tank specialization", "Tank specialization")
        },
        ["talents/priority"] = new[]
        {
            new ConfigChoice("All main attributes", "All main attributes"),
            new ConfigChoice("Attribute damage", "Attribute damage"),
            new ConfigChoice("Attribute health", "Attribute health"),
            new ConfigChoice("Attribute armor", "Attribute armor"),
            new ConfigChoice("Leadership", "Leadership"),
            new ConfigChoice("Guardian power", "Guardian power"),
            new ConfigChoice("Team bonus", "Team bonus"),
            new ConfigChoice("Leader - Auto Abilities", "Leader - Auto Abilities"),
            new ConfigChoice("Party - Auto Abilities", "Party - Auto Abilities"),
            new ConfigChoice("Attack speed", "Attack speed"),
            new ConfigChoice("Trainer skills", "Trainer skills"),
            new ConfigChoice("Critical chance", "Critical chance"),
            new ConfigChoice("Critical damage", "Critical damage"),
            new ConfigChoice("Dodge", "Dodge"),
            new ConfigChoice("Librarian", "Librarian"),
            new ConfigChoice("Meteorite hunter", "Meteorite hunter"),
            new ConfigChoice("Expeditioner", "Expeditioner"),
            new ConfigChoice("Powerless enemy", "Powerless enemy"),
            new ConfigChoice("Powerless boss", "Powerless boss"),
            new ConfigChoice("Weaklings", "Weaklings"),
            new ConfigChoice("Expose weakness", "Expose weakness"),
            new ConfigChoice("Ancient knowledge", "Ancient knowledge"),
            new ConfigChoice("Raining gold", "Raining gold"),
            new ConfigChoice("Coworkers", "Coworkers"),
            new ConfigChoice("Twin dragons", "Twin dragons"),
            new ConfigChoice("Battle cry", "Battle cry"),
            new ConfigChoice("Alchemy", "Alchemy"),
            new ConfigChoice("Fate", "Fate"),
            new ConfigChoice("Mana heroes", "Mana heroes"),
            new ConfigChoice("Energy heroes", "Energy heroes"),
            new ConfigChoice("Rage heroes", "Rage heroes"),
            new ConfigChoice("Damage specialization", "Damage specialization"),
            new ConfigChoice("Tank specialization", "Tank specialization"),
            new ConfigChoice("Healer specialization", "Healer specialization"),
            new ConfigChoice("Fist fight", "Fist fight"),
            new ConfigChoice("Precision", "Precision"),
            new ConfigChoice("Magic spells", "Magic spells")
        },
        ["meteorite_research/priority"] = new[]
        {
            new ConfigChoice("Attribute damage", "Attribute damage"),
            new ConfigChoice("All main attributes", "All main attributes"),
            new ConfigChoice("Raining gold", "Raining gold"),
            new ConfigChoice("Hero level up cost", "Hero level up cost"),
            new ConfigChoice("Attribute health", "Attribute health"),
            new ConfigChoice("Attribute armor", "Attribute armor"),
            new ConfigChoice("Firestone finder", "Firestone finder"),
            new ConfigChoice("Powerless boss", "Powerless boss"),
            new ConfigChoice("Powerless enemy", "Powerless enemy")
        },
        ["bubble_rewards/bubbles"] = new[]
        {
            new ConfigChoice("SignIn", "SignIn"),
            new ConfigChoice("LockerRewards", "LockerRewards"),
            new ConfigChoice("PiratesPrize", "PiratesPrize"),
            new ConfigChoice("HallOfHeroes", "HallOfHeroes"),
            new ConfigChoice("TowerOfSouls", "TowerOfSouls"),
            new ConfigChoice("Prophet", "Prophet"),
            new ConfigChoice("Mercenaries", "Mercenaries"),
            new ConfigChoice("AncientArtifact", "AncientArtifact"),
            new ConfigChoice("TalentAvailable", "TalentAvailable"),
            new ConfigChoice("GuardianEvolution", "GuardianEvolution"),
            new ConfigChoice("GuardianRarity", "GuardianRarity"),
            new ConfigChoice("WarMachinesRarity", "WarMachinesRarity"),
            new ConfigChoice("WarmachineUnlocked", "WarmachineUnlocked"),
            new ConfigChoice("ScarabGameBeastRelease", "ScarabGameBeastRelease"),
            new ConfigChoice("UnlockLedra", "UnlockLedra"),
            new ConfigChoice("UnlockYamanoth", "UnlockYamanoth"),
            new ConfigChoice("UnlockKramatak", "UnlockKramatak"),
            new ConfigChoice("BattleLive", "BattleLive"),
            new ConfigChoice("ChaosRiftSupplies", "ChaosRiftSupplies"),
            new ConfigChoice("GuardianHolyUpgrade", "GuardianHolyUpgrade")
        },
        ["alchemist/resource_type"] = new[]
        {
            new ConfigChoice("0", "Dragon blood"),
            new ConfigChoice("1", "Strange dust"),
            new ConfigChoice("2", "Exotic coin")
        },

        // The id is the zero-based slot the .cfg stores; the label is the name the game shows for that
        // slot. Both are declared here because the file keeps its own format.
        ["auto_upgrade/upgrade_target_slots"] = new[]
        {
            new ConfigChoice("0", "Specials"),
            new ConfigChoice("1", "Guardian"),
            new ConfigChoice("2", "1"),
            new ConfigChoice("3", "2"),
            new ConfigChoice("4", "3"),
            new ConfigChoice("5", "4"),
            new ConfigChoice("6", "5")
        }
    };

    /// <summary>
    ///     Entries holding an ordered list of steps, where order and repetition count. A text field takes the
    ///     sequence and says nothing about what it understood, which is how a typo silently dropped a step,
    ///     so these get a widget that shows the steps and appends them one at a time.
    /// </summary>
    private static readonly HashSet<string> ComboSteps = new()
    {
        "auto_skill/combo_sequence"
    };

    /// <summary>
    ///     Text shown in the panel's description box, for the entries whose .cfg description does not fit it.
    ///     <para>
    ///         The description of an entry is the comment MelonLoader writes in the file, so it is written
    ///         for the file: it can carry the full manual, examples included. The box holds roughly three
    ///         lines and truncates the rest with an ellipsis, which means the useful tail — the examples —
    ///         was the part being cut. Declaring the panel text here keeps each surface in its own format,
    ///         and the file stays exactly as it was.
    ///     </para>
    /// </summary>
    private static readonly Dictionary<string, string> PanelTexts = new()
    {
        // Settings
        ["firebot_settings/start_bot_delay"] =
            "Cooldown in seconds before the bot starts.\n" +
            "Keeps it from acting while Unity is still loading the first scene.\n" +
            "Allowed range: 10 to 120.",

        ["firebot_settings/scan_interval"] =
            "Seconds between each verification cycle of the bot.\n" +
            "Lower is more responsive and costs more FPS.\n" +
            "Allowed range: 5 to 3600.",

        ["firebot_settings/interaction_delay"] =
            "Delay in seconds between individual UI interactions.\n" +
            "Gives the game time to process a command before the next one.\n" +
            "Allowed range: 0.5 to 5.",

        ["firebot_settings/free_speedup_seconds"] =
            "Timers below this many seconds can be sped up for free (no gems).\n" +
            "0 disables it; the game caps it at 180 (3 minutes).\n" +
            "Affects research, missions, experiments and map reset.",

        // Tasks
        ["temple_prestige/enabled"] =
            "Uses only the free empower option. The bot must also be running.\n" +
            "The minimum boost and run age apply to both triggers.",

        ["temple_prestige/stall_hours"] =
            "Consider a reset after this many hours without a new highest stage.\n" +
            "Both minimums must still be met.",

        ["temple_prestige/min_gain_percent"] =
            "Minimum extra gold boost on the free empower preview. 0 removes the minimum.\n" +
            "The bot never resets when the boost is unreadable.",

        ["temple_prestige/min_hours_played"] =
            "Minimum age of the current run in hours. 0 removes the minimum.\n" +
            "The bot never resets when the run age is unreadable.",

        ["temple_prestige/efficiency_percent"] =
            "Two hourly samples below this share of the run's average growth can trigger a reset.\n" +
            "This is an estimate, not a proven optimum. 0 disables this trigger.",

        ["alchemist/resource_type"] =
            "HOW TO USE: Toggle which resources an experiment may use.\n" +
            "'Dragon blood' = 0, 'Strange dust' = 1, 'Exotic coin' = 2.\n" +
            "No resource marked disables the task.",

        ["firestone_research/research_priority"] =
            "Numbers show priority. Tap to remove, then tap to add last.\n" +
            "Names describe Tree I; positions also apply to other trees.\n" +
            "An empty list picks any available research.",

        ["personal_tree/priority"] =
            "Numbers show priority. Tap to remove, then tap to add last.\n" +
            "Fill each available listed upgrade before the next.",

        ["forbidden_knowledge/priority"] =
            "Numbers show priority. Tap to remove, then tap to add last.\n" +
            "Fill each available listed upgrade before the next.",

        ["meteorite_research/priority"] =
            "Numbers show priority. Tap to remove, then tap to add last.\n" +
            "Fill each available listed upgrade before the next.",

        ["talents/priority"] =
            "Numbers show priority. Tap to remove, then tap to add last.\n" +
            "Fill each available listed talent before the next.",

        ["oracle_blessings/priority"] =
            "Numbers show priority. Tap to remove, then tap to add last.\n" +
            "Prioritize the blessings that suit your party.",

        ["exotic_merchant/priority"] =
            "Numbers show priority. Tap to remove, then tap to add last.\n" +
            "Level-locked upgrades wait until you can unlock them.",

        ["war_machine_upgrade/war_machine"] =
            "Numbers show machine order. Tap to remove, then tap to add last.\n" +
            "Active squad expands to the machines in your current squad.",

        ["war_machine_upgrade/blueprint_priority"] =
            "When Blueprints is set to All, buy these types in numbered order.\n" +
            "Tap to remove, then tap to add last; an empty list buys none.",

        ["chaos_rift/holy_guardians"] =
            "Numbers show guardian priority. Tap to remove, then tap to add last.\n" +
            "The holy upgrade strategy decides whether lower priorities can spend.",

        ["chaos_rift/holy_priority_mode"] =
            "Balance checks each selected guardian in order.\n" +
            "Focus first saves orbs for the first unlocked guardian, even when it cannot afford an upgrade.",

        ["event_exchange/priority"] =
            "Numbers show buying order. Tap to remove, then tap to add last.\n" +
            "Unlisted items are not bought.",

        ["free_pickaxes/pickaxe_claim_threshold"] =
            "Claim only when at least this many free pickaxes are stored.\n" +
            "Set 1 to claim as soon as one is available, or 30 to wait for the maximum.",

        ["auto_skill/combo_sequence"] =
            "HOW TO USE: +1/+2/+3 append steps, Undo removes, Clear empties.\n" +
            "Steps play in order and repeat forever: 1, 2, 3 only, max 10.\n" +
            "An empty sequence plays nothing.",

        ["auto_upgrade/upgrade_target_slots"] =
            "Numbers show upgrade order. Tap to remove, then tap to add last.\n" +
            "An empty list uses all slots in screen order.",

        ["auto_upgrade/priority_mode"] =
            "Balance upgrades selected slots in order each burst.\n" +
            "Focus first saves gold for the first visible slot, even when it cannot afford an upgrade."
    };

    /// <summary>Reads the sections of our file, in the order MelonLoader registered them.</summary>
    public static List<ConfigGroup> Read()
    {
        var groups = new List<ConfigGroup>();

        SeenKeys.Clear();

        var categories = MelonPreferences.Categories;
        if (categories == null) return groups;

        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            if (!BelongsToConfigFile(category)) continue;

            var entries = ReadEntries(category);
            if (entries.Count == 0) continue;

            groups.Add(new ConfigGroup(category, entries));
        }

        // Without this, a filter failure would show up as an empty, silent panel.
        if (groups.Count == 0 && categories.Count > 0)
            Logger.Warning(
                $"[UI] None of the {categories.Count} registered categories matches '{BotSettings.ConfigPath}'.");

        ReportUnmatchedDeclarations();
        return groups;
    }

    /// <summary>
    ///     Warns about declarations that never matched an entry. A typo in one of the tables below is
    ///     otherwise silent: the entry still exists, it just loses its widget and falls back to a slider
    ///     or a text field. Reported once per session, and only when the filter actually found entries —
    ///     otherwise every declaration would look unmatched.
    /// </summary>
    private static void ReportUnmatchedDeclarations()
    {
        if (_unmatchedDeclarationsLogged || SeenKeys.Count == 0) return;
        _unmatchedDeclarationsLogged = true;

        ReportUnmatched(DeclaredRanges.Keys, nameof(DeclaredRanges));
        ReportUnmatched(SingleChoices.Keys, nameof(SingleChoices));
        ReportUnmatched(MultiChoices.Keys, nameof(MultiChoices));
        ReportUnmatched(ComboSteps, nameof(ComboSteps));
        ReportUnmatched(PanelTexts.Keys, nameof(PanelTexts));
    }

    private static void ReportUnmatched(IEnumerable<string> declared, string table)
    {
        foreach (var key in declared)
            if (!SeenKeys.Contains(key))
                Logger.Warning($"[UI] '{key}' is declared in {table} but matches no entry; check the spelling.");
    }

    /// <summary>
    ///     Key of another keybind entry of our file that is already bound to <paramref name="key" />, or
    ///     null when the key is free.
    ///     <para>
    ///         It reads the live MelonPreferences entries — the same objects BotSettings and the bot
    ///         actions read — instead of the built rows: a collapsed section still holds its rows, but the
    ///         panel may not be built at all, and the .cfg can also be edited outside the game.
    ///     </para>
    /// </summary>
    public static string FindKeybindOwner(KeyCode key, string exceptKey)
    {
        var categories = MelonPreferences.Categories;
        if (categories == null) return null;

        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            if (!BelongsToConfigFile(category)) continue;

            var entries = category.Entries;
            if (entries == null) continue;

            for (var j = 0; j < entries.Count; j++)
            {
                var entry = entries[j];
                if (entry == null || entry.IsHidden) continue;
                if (ConfigEntry.DeclaredTypeOf(entry) != typeof(KeyCode)) continue;
                if (entry.BoxedValue is not KeyCode bound || bound != key) continue;

                var owner = $"{category.Identifier}/{entry.Identifier}";
                if (owner != exceptKey) return owner;
            }
        }

        return null;
    }

    // ---- Debounced persistence ----
    // Dragging a slider fires onValueChanged every frame; writing the file at that rate would be
    // absurd. The in-memory value is updated immediately and the file is saved once the user stops
    // interacting.

    public static void MarkDirty(MelonPreferences_Category category)
    {
        if (category == null) return;

        DirtyCategories.Add(category);
        _hasPendingSave = true;
        _lastChangeAt = Time.unscaledTime;
    }

    public static void FlushPendingSaves()
    {
        if (!_hasPendingSave) return;
        if (Time.unscaledTime - _lastChangeAt < SaveDelaySeconds) return;

        _hasPendingSave = false;

        foreach (var category in DirtyCategories)
            try
            {
                category.SaveToFile();
            }
            catch (Exception e)
            {
                Logger.Warning($"[UI] Failed to save '{category.Identifier}': {e.GetType().Name} - {e.Message}");
            }

        DirtyCategories.Clear();
    }

    public static void LogUnwritable(MelonPreferences_Category category, MelonPreferences_Entry entry)
    {
        var key = $"{category.Identifier}/{entry.Identifier}";
        if (!ReportedUnwritable.Add(key)) return;

        Logger.Warning($"[UI] '{key}' has no writable Value property; showing it read-only.");
    }

    // ---- Internals ----

    /// <summary>Whether the entry is drawn by the step widget instead of the one its type implies.</summary>
    private static bool IsComboSteps(MelonPreferences_Category category, MelonPreferences_Entry entry)
        => ComboSteps.Contains($"{category.Identifier}/{entry.Identifier}");

    /// <summary>Text the panel shows for the entry, or null to fall back to the .cfg description.</summary>
    private static string ResolvePanelText(MelonPreferences_Category category, MelonPreferences_Entry entry)
        => PanelTexts.TryGetValue($"{category.Identifier}/{entry.Identifier}", out var text) ? text : null;

    private static List<ConfigEntry> ReadEntries(MelonPreferences_Category category)
    {
        var result = new List<ConfigEntry>();

        var source = category.Entries;
        if (source == null) return result;

        for (var i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            if (entry == null || entry.IsHidden) continue;

            var declared = ConfigEntry.DeclaredTypeOf(entry);
            var config = new ConfigEntry(category, entry, ResolveRange(category, entry, declared),
                ResolveChoices(category, entry, declared), IsComboSteps(category, entry),
                ResolvePanelText(category, entry));

            SeenKeys.Add(config.Key);
            result.Add(config);
        }

        return result;
    }

    /// <summary>
    ///     Options declared for an entry. Without this the UI has no way to discover that
    ///     'map_missions/mission_time_order' is a choice: to MelonLoader it is just a string.
    ///     The id is what goes into the .cfg — the name exists only on screen.
    /// </summary>
    private static ChoiceSet ResolveChoices(MelonPreferences_Category category, MelonPreferences_Entry entry,
        Type declaredType)
    {
        var key = $"{category.Identifier}/{entry.Identifier}";
        var isNumeric = declaredType == typeof(int) || declaredType == typeof(float);

        if (SingleChoices.TryGetValue(key, out var single))
        {
            if (declaredType == typeof(string) || isNumeric) return new ChoiceSet(single, false);

            Logger.Warning(
                $"[UI] '{key}' declares single choices but its type is {declaredType?.Name ?? "null"}; ignoring them.");
            return null;
        }

        if (!MultiChoices.TryGetValue(key, out var multiple)) return null;

        if (declaredType == typeof(string)) return new ChoiceSet(multiple, true);

        // Without the warning this would become a chip widget that can only store one of the marked
        // ids.
        Logger.Warning(
            $"[UI] '{key}' declares multiple choices but its type is {declaredType?.Name ?? "null"}; " +
            "a number cannot hold a list. Declare it in SingleChoices instead.");
        return null;
    }

    private static NumberRange ResolveRange(MelonPreferences_Category category, MelonPreferences_Entry entry,
        Type declaredType)
    {
        var isNumeric = declaredType == typeof(float) || declaredType == typeof(int);
        if (!isNumeric) return default;

        var key = $"{category.Identifier}/{entry.Identifier}";
        if (DeclaredRanges.TryGetValue(key, out var declared)) return declared;

        // No declared range: derive a conservative window from the current value and warn in the
        // log, so the gap shows up instead of becoming a slider that accepts anything.
        var isInt = declaredType == typeof(int);
        var current = Convert.ToSingle(entry.BoxedValue ?? 0f);
        var max = current <= 0f ? (isInt ? 10f : 100f) : current * 2f;

        Logger.Debug($"[UI] No declared range for '{key}'; using 0..{max:0.##}. Add it to DeclaredRanges.");
        return new NumberRange(0f, max, isInt);
    }

    private static bool BelongsToConfigFile(MelonPreferences_Category category)
    {
        if (category == null) return false;

        if (CategoryFileField == null)
        {
            LogFilterFailure("MelonPreferences_Category no longer has a 'File' field");
            return false;
        }

        var ours = CategoryFileField.GetValue(BotSettings.Category);
        if (ours == null)
        {
            LogFilterFailure("the firebot_settings category has no config file attached");
            return false;
        }

        return ReferenceEquals(CategoryFileField.GetValue(category), ours);
    }

    private static void LogFilterFailure(string reason)
    {
        if (_filterFailureLogged) return;

        _filterFailureLogged = true;
        Logger.Error($"[UI] Cannot tell which categories belong to the bot config ({reason}); showing no sections.");
    }
}
