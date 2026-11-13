using System.Collections;
using System.Reflection;
using Firebot.Core;
using Firebot.Core.Tasks;
using Il2Cpp;

/// <summary>A fresh account: features the game has not handed out yet, and the guided tutorial.</summary>
internal static class NewAccountTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Account(int level)
    {
        GameInitialize.Loaded = true;
        GameInitialize.HandlerLoader = new();
        GameInitialize.HandlerLoader.CharacterLevel.Level = level;
        GameInitialize.MenuLoader = new();
        TutorialGuidance.TutorialIsolation.Forced = false;
        _ = PlayerStats.CharacterLevel;
        Logger.Messages.Clear();
    }

    private static void Restore()
    {
        GameInitialize.Loaded = true;
        GameInitialize.HandlerLoader = new();
        GameInitialize.MenuLoader = new();
        TutorialGuidance.TutorialIsolation.Forced = false;
        _ = PlayerStats.CharacterLevel;
        _ = GameTutorial.IsActive;
    }

    public static void FeatureGating()
    {
        try
        {
            Account(12);
            var unlocks = GameInitialize.HandlerLoader.UnlockMechanismsData;
            unlocks.TavernUnlocked.Value = false;
            var task = new TavernGateTask();
            task.InitializeConfig("test");
            task.SetEnabledFromStatus(true);
            task.RestoreNextRun(DateTime.Now.AddSeconds(-1));
            Check(GameUnlocks.Check(GameFeature.Tavern) == false && task.IsFeatureLocked && task.IsLocked &&
                  !task.IsReady() && !task.IsReady(true) && !task.IsNotificationVisible(),
                "A task ran before the game unlocked its feature.");

            var open = new OpenTask();
            open.InitializeConfig("test");
            open.SetEnabledFromStatus(true);
            open.RestoreNextRun(DateTime.Now.AddSeconds(-1));
            Check(!open.IsLocked && open.IsReady(), "A task with no requirement was held by a locked feature.");

            unlocks.TavernUnlocked.Value = true;
            Check(!task.IsLocked && task.IsReady() &&
                  Logger.Messages.Contains("[Unlocks] The game just unlocked Tavern."),
                "The task did not resume, or say so, the moment the game unlocked its feature.");

            unlocks.TavernUnlocked.Value = false;
            GameInitialize.HandlerLoader.CharacterLevel.Level = GameUnlocks.TrustedLevel;
            Check(!task.IsLocked && task.IsReady(),
                "An established account was held back by an unlock flag.");

            GameInitialize.HandlerLoader.CharacterLevel.Level = 12;
            _ = PlayerStats.CharacterLevel;
            GameInitialize.HandlerLoader.UnlockMechanismsData = null;
            Check(GameUnlocks.Check(GameFeature.Tavern) == null && task.IsLocked,
                "Missing unlock data was read as unlocked on a low-level account.");

            typeof(PlayerStats).GetField("_lastKnownLevel", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, 0);
            GameInitialize.HandlerLoader = new();
            GameInitialize.HandlerLoader.CharacterLevel.hasLoaded = false;
            Check(GameUnlocks.Check(GameFeature.Tavern) == null && task.IsLocked && !task.IsReady(),
                "A task ran before the character level was known.");

            Account(12);
            GameInitialize.HandlerLoader.UnlockMechanismsData.ThrowOnRead = true;
            Check(GameUnlocks.Check(GameFeature.Liberation) == true &&
                  GameUnlocks.Check(GameFeature.Liberation) == true &&
                  Logger.Messages.Count(m => m.Contains("[Unlocks] Could not read")) == 1,
                "A failed unlock read silenced the task, or warned on every scan.");
        }
        finally
        {
            Restore();
        }
    }

    public static void TutorialState()
    {
        try
        {
            Account(5);
            var guide = GameInitialize.MenuLoader.TutorialGuidance;
            guide.isTutorialFinished = false;
            guide.isCurrentTutorialPartInProgress = true;
            guide.currentTutorial = 7;
            Check(GameTutorial.IsActive && GameTutorial.IsActive &&
                  Logger.Messages.Count(m => m.StartsWith("[Tutorial] The game's tutorial is running (step 7)")) == 1,
                "A running tutorial step was missed, or logged on every read.");

            guide.isCurrentTutorialPartInProgress = false;
            Check(!GameTutorial.IsActive && Logger.Messages.Any(m => m.StartsWith("[Tutorial] Tutorial step done")),
                "The bot kept waiting after the tutorial step ended.");

            TutorialGuidance.TutorialIsolation.Forced = true;
            Check(GameTutorial.IsActive && GameTutorial.IsForced &&
                  Logger.Messages.Any(m => m.Contains("highlighted")),
                "A forced tutorial highlight did not hold the bot.");

            guide.isTutorialFinished = true;
            Check(!GameTutorial.IsActive && GameTutorial.IsForced && GameTutorial.ShouldHold,
                "A forced feature introduction on a new account did not hold the bot.");

            GameInitialize.HandlerLoader.CharacterLevel.Level = GameTutorial.TrustedLevel;
            Check(!GameTutorial.ShouldHold, "An established account was held by a feature highlight.");
            GameInitialize.HandlerLoader.CharacterLevel.Level = 5;

            guide.isTutorialFinished = false;
            GameInitialize.HandlerLoader.CharacterLevel.Level = GameTutorial.TrustedLevel;
            Check(!GameTutorial.IsActive, "An established account was held by a stale tutorial flag.");

            GameInitialize.MenuLoader.TutorialGuidance = null;
            GameInitialize.HandlerLoader.CharacterLevel.Level = 5;
            Check(!GameTutorial.IsActive, "A missing tutorial guide held the bot.");
        }
        finally
        {
            Restore();
        }
    }

    private sealed class TavernGateTask : BotTask
    {
        public override GameFeature RequiredFeature => GameFeature.Tavern;
        public override IEnumerator Execute() { yield break; }
    }

    private sealed class OpenTask : BotTask
    {
        public override IEnumerator Execute() { yield break; }
    }
}
