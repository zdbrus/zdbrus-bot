using System;
using System.Collections.Generic;
using Il2Cpp;

namespace Firebot.Core;

/// <summary>A game feature a task needs before it can run. <see cref="None" /> means it is there from the start.</summary>
public enum GameFeature
{
    None,
    Alchemist,
    ArenaOfKings,
    Awakening,
    BattlePass,
    ChaosRift,
    CharacterTalents,
    EventManager,
    ExoticMerchant,
    GuardianStrengthen,
    Guilds,
    InventoryBag,
    Liberation,
    Library,
    Oracle,
    Quests,
    ScarabGame,
    Store,
    Tavern,
    TempleOfEternals,
    Upgrades,
    WorldMap
}

/// <summary>
///     The game's own "is this unlocked yet" flags, so a new account does not walk into menus that do not exist.
///     <para>
///         Three answers, not two: while the game or the character level is still loading the answer is
///         <c>null</c>, and a caller holds back without reporting a lock. A read that throws counts as unlocked
///         (logged once) — a broken read must not silence the bot on an account that has everything.
///     </para>
/// </summary>
public static class GameUnlocks
{
    /// <summary>
    ///     From this character level on the flags are not consulted at all, so an established account behaves
    ///     exactly as it did before the flags were read.
    /// </summary>
    public const int TrustedLevel = 200;

    private static readonly HashSet<string> LoggedFailures = new();
    private static readonly Dictionary<GameFeature, bool> LastSeen = new();

    /// <summary>True when unlocked, false when locked, null while the game cannot say yet.</summary>
    public static bool? Check(GameFeature feature)
    {
        if (feature == GameFeature.None) return true;
        if (!PlayerStats.TryGetCharacterLevel(out var level)) return null;
        if (level >= TrustedLevel) return true;

        try
        {
            if (!GameInitialize.HasGameLoaded()) return null;
            var data = GameInitialize.HandlerLoader?.UnlockMechanismsData;
            if (data == null) return null;

            var unlocked = Read(data, feature);
            Track(feature, unlocked);
            return unlocked;
        }
        catch (Exception e)
        {
            if (LoggedFailures.Add($"{feature}|{e.GetType().Name}|{e.Message}"))
                Logger.Warning($"[Unlocks] Could not read whether {DisplayName(feature)} is unlocked " +
                               $"({e.GetType().Name} - {e.Message}); treating it as unlocked.");
            return true;
        }
    }

    public static bool IsUnlocked(GameFeature feature) => Check(feature) == true;

    /// <summary>For the log and the settings note.</summary>
    public static string DisplayName(GameFeature feature) => feature switch
    {
        GameFeature.ArenaOfKings => "Arena of Kings",
        GameFeature.BattlePass => "Battle Pass",
        GameFeature.ChaosRift => "Chaos Rift",
        GameFeature.CharacterTalents => "Talents",
        GameFeature.EventManager => "Events",
        GameFeature.ExoticMerchant => "Exotic Merchant",
        GameFeature.GuardianStrengthen => "Guardian training",
        GameFeature.Guilds => "Guilds",
        GameFeature.InventoryBag => "the Bag",
        GameFeature.Liberation => "Liberation missions",
        GameFeature.ScarabGame => "the Scarab game",
        GameFeature.TempleOfEternals => "Temple of Eternals",
        GameFeature.WorldMap => "the World Map",
        _ => feature.ToString()
    };

    /// <summary>For the status column, which is narrow.</summary>
    public static string ShortName(GameFeature feature) => feature switch
    {
        GameFeature.ArenaOfKings => "Arena",
        GameFeature.BattlePass => "Pass",
        GameFeature.ChaosRift => "Rift",
        GameFeature.CharacterTalents => "Talents",
        GameFeature.EventManager => "Events",
        GameFeature.ExoticMerchant => "Merchant",
        GameFeature.GuardianStrengthen => "Training",
        GameFeature.Guilds => "Guild",
        GameFeature.InventoryBag => "Bag",
        GameFeature.Liberation => "Missions",
        GameFeature.ScarabGame => "Scarab",
        GameFeature.TempleOfEternals => "Temple",
        GameFeature.WorldMap => "Map",
        _ => feature.ToString()
    };

    private static bool Read(UnlockMechanismsData d, GameFeature feature) => feature switch
    {
        GameFeature.Alchemist => d.AlchemistUnlocked.GetDecrypted(),
        GameFeature.ArenaOfKings => d.ArenaOfKingsUnlocked.GetDecrypted(),
        GameFeature.Awakening => d.AwakeningUnlocked.GetDecrypted(),
        GameFeature.BattlePass => d.BattlePassUnlocked.GetDecrypted(),
        GameFeature.ChaosRift => d.ChaosRiftUnlocked.GetDecrypted(),
        GameFeature.CharacterTalents => d.CharacterTalentsUnlocked.GetDecrypted(),
        GameFeature.EventManager => d.EventManagerUnlocked.GetDecrypted(),
        GameFeature.ExoticMerchant => d.ExoticMerchantUnlocked.GetDecrypted(),
        GameFeature.GuardianStrengthen => d.GuardianStrengthenUnlocked.GetDecrypted(),
        GameFeature.Guilds => d.GuildsUnlocked.GetDecrypted(),
        GameFeature.InventoryBag => d.InventoryBagUnlocked.GetDecrypted(),
        GameFeature.Liberation => d.IsLiberationUnlocked(),
        GameFeature.Library => d.LibraryUnlocked.GetDecrypted(),
        GameFeature.Oracle => d.OracleUnlocked.GetDecrypted(),
        GameFeature.Quests => d.QuestsUnlocked.GetDecrypted(),
        GameFeature.ScarabGame => d.ScarabGameUnlocked.GetDecrypted(),
        GameFeature.Store => d.StoreUnlocked.GetDecrypted(),
        GameFeature.Tavern => d.TavernUnlocked.GetDecrypted(),
        GameFeature.TempleOfEternals => d.TempleOfEternalsUnlocked.GetDecrypted(),
        GameFeature.Upgrades => d.UpgradesUnlocked.GetDecrypted(),
        GameFeature.WorldMap => d.WorldMapUnlocked.GetDecrypted(),
        _ => true
    };

    /// <summary>Says so once when a feature the bot was waiting on opens up.</summary>
    private static void Track(GameFeature feature, bool unlocked)
    {
        var known = LastSeen.TryGetValue(feature, out var was);
        if (known && was == unlocked) return;

        LastSeen[feature] = unlocked;
        if (known && unlocked) Logger.Info($"[Unlocks] The game just unlocked {DisplayName(feature)}.");
    }
}

/// <summary>
///     The game's guided tutorial on a new account. While a tutorial step is running the bot keeps its hands off:
///     the step waits for the player to click one highlighted thing, and a click the bot sends elsewhere either
///     goes nowhere or knocks the step out of order.
/// </summary>
public static class GameTutorial
{
    /// <summary>The tutorial is long over by this character level; past it the flags are not read.</summary>
    public const int TrustedLevel = 100;

    private static readonly HashSet<string> LoggedFailures = new();
    private static bool _wasActive;
    private static DateTime _activeSince;
    private static DateTime _lastReminder;

    /// <summary>A tutorial step is on screen and waiting for the player.</summary>
    public static bool IsActive
    {
        get
        {
            var active = Read(out var detail);
            var now = DateTime.Now;

            if (active != _wasActive)
            {
                _wasActive = active;
                if (active)
                {
                    _activeSince = _lastReminder = now;
                    Logger.Info($"[Tutorial] The game's tutorial is running ({detail}). The bot waits until " +
                                "the step is done.");
                }
                else
                    Logger.Info($"[Tutorial] Tutorial step done after {(now - _activeSince).TotalSeconds:0}s; " +
                                "the bot carries on.");
            }
            else if (active && now - _lastReminder >= TimeSpan.FromMinutes(10))
            {
                _lastReminder = now;
                Logger.Info($"[Tutorial] Still waiting on the game's tutorial after " +
                            $"{(now - _activeSince).TotalMinutes:0} min ({detail}). Finish the highlighted step " +
                            "and the bot carries on.");
            }

            return active;
        }
    }

    /// <summary>
    ///     Whether the bot keeps its hands off: a tutorial step is running, or, below <see cref="TrustedLevel" />,
    ///     the game is forcing a feature introduction (the same highlight, after the tutorial is over). The
    ///     introduction usually appears in the same scan that unlocks the feature, which is exactly when its
    ///     never-run task becomes ready.
    /// </summary>
    public static bool ShouldHold
    {
        get
        {
            if (IsActive) return true;
            return PlayerStats.TryGetCharacterLevel(out var level) && level < TrustedLevel && IsForced;
        }
    }

    /// <summary>
    ///     The tutorial highlight is up and only the highlighted button may be pressed. Read on its own, without
    ///     the "tutorial finished" check, because the game uses the same highlight for feature introductions.
    /// </summary>
    public static bool IsForced
    {
        get
        {
            try
            {
                return GameInitialize.HasGameLoaded() && TutorialGuidance.TutorialIsolation.IsForcedTutorialOpen();
            }
            catch (Exception e)
            {
                FailOnce($"forced|{e.GetType().Name}|{e.Message}",
                    $"Could not read the tutorial highlight: {e.GetType().Name} - {e.Message}");
                return false;
            }
        }
    }

    private static bool Read(out string detail)
    {
        detail = "";
        if (PlayerStats.TryGetCharacterLevel(out var level) && level >= TrustedLevel) return false;

        try
        {
            if (!GameInitialize.HasGameLoaded()) return false;
            var guide = GameInitialize.MenuLoader?.TutorialGuidance;
            if (guide == null || guide.isTutorialFinished || guide.HasFinishedTutorial()) return false;

            var forced = TutorialGuidance.TutorialIsolation.IsForcedTutorialOpen();
            var inProgress = guide.isCurrentTutorialPartInProgress;
            if (!forced && !inProgress) return false;

            detail = $"step {guide.currentTutorial}{(forced ? ", highlighted" : "")}";
            return true;
        }
        catch (Exception e)
        {
            FailOnce($"{e.GetType().Name}|{e.Message}",
                $"Could not read the tutorial state: {e.GetType().Name} - {e.Message}");
            return false;
        }
    }

    private static void FailOnce(string key, string message)
    {
        if (LoggedFailures.Add(key)) Logger.Warning($"[Tutorial] {message}");
    }
}
