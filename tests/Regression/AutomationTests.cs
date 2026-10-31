using System.Collections;
using System.Reflection;
using Firebot.BotActions;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.Infrastructure;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static class AutomationTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static MelonPreferences_Entry<T> Entry<T>(string category, string id) =>
        (MelonPreferences_Entry<T>)MelonPreferences.Categories[category].Entries[id];

    private static object Handle(Type type, string name) => type.GetField(name, PrivateStatic).GetValue(null);

    private static void Boot(bool optIn)
    {
        AutoUpgrade.OnSceneChanged(false);
        AutoSkill.OnSceneChanged(false);
        GameObject.Clear();
        GameObject.BeforeFind = null;
        ExecuteEvents.OnDown = null;
        Input.Pressed = KeyCode.None;
        HotkeyGate.IsCapturing = false;
        MelonPreferences.SavedValues.Clear();
        MelonPreferences.SavedValues["firebot_settings.enable_all_on_launch"] = optIn;
        MelonPreferences.SavedValues["firebot_settings.auto_start"] = false;
        MelonPreferences.SavedValues["firebot_settings.auto_update"] = false;
        MelonPreferences.SavedValues["auto_upgrade.enabled"] = false;
        MelonPreferences.SavedValues["auto_skill.enabled"] = false;
        BotSettings.Initialize();
        typeof(AutoUpgrade).GetField("_isInitialized", PrivateStatic).SetValue(null, false);
        typeof(AutoSkill).GetField("_isInitialized", PrivateStatic).SetValue(null, false);
        AutoUpgrade.Initialize();
        AutoSkill.Initialize();
    }

    public static void LaunchOverrides()
    {
        Boot(false);
        Check(!BotSettings.AutoStart && !Entry<bool>("auto_upgrade", "enabled").Value &&
              !Entry<bool>("auto_skill", "enabled").Value, "Normal saved off choices were overridden.");
        var off = new LaunchTask();
        off.InitializeConfig("test.cfg");
        Check(!off.IsEnabled, "An opt-out player had a task enabled.");
        MelonPreferences.SavedValues["auto_upgrade.enabled"] = true;
        typeof(AutoUpgrade).GetField("_isInitialized", PrivateStatic).SetValue(null, false);
        AutoUpgrade.Initialize();
        Check(Entry<bool>("auto_upgrade", "enabled").Value, "Opt-out lost a saved on choice.");

        Boot(true);
        Check(BotSettings.AutoStart && Entry<bool>("auto_upgrade", "enabled").Value &&
              Entry<bool>("auto_skill", "enabled").Value, "Launch opt-in did not enable the main automations.");
        MelonPreferences.SavedValues["launch.reserve"] = 1234;
        MelonPreferences.SavedValues["launch.optional_spending"] = false;
        MelonPreferences.SavedValues["launch.priority"] = "health,damage";
        var task = new LaunchTask();
        task.InitializeConfig("test.cfg");
        Check(task.IsEnabled && Entry<int>("launch", "reserve").Value == 1234 &&
              !Entry<bool>("launch", "optional_spending").Value &&
              Entry<string>("launch", "priority").Value == "health,damage",
            "Launch override missed a task or changed its spending preferences.");
        var arena = new Firebot.Behaviors.Extra.ArenaTask();
        arena.InitializeConfig("test.cfg");
        Check(arena.IsEnabled && !Entry<bool>("arena", "record_matchups").Value,
            "Enable All On Launch turned on optional matchup recording.");
        task.Disable();
        task.InitializeConfig("test.cfg");
        Check(!task.IsEnabled, "Launch settings were reapplied within the current process.");
        var nextLaunch = new LaunchTask();
        nextLaunch.InitializeConfig("test.cfg");
        Check(nextLaunch.IsEnabled, "The next launch did not restore an enabled task.");
    }

    public static void StatusTaskToggle()
    {
        Boot(false);
        var task = new LaunchTask();
        task.InitializeConfig("test.cfg");

        Check(task.SetEnabledFromStatus(true) && task.IsEnabled &&
              Entry<bool>("launch", "enabled").Value,
            "The status switch did not update the task's shared enabled setting.");
        Check(task.SetEnabledFromStatus(false) && !task.IsEnabled &&
              !Entry<bool>("launch", "enabled").Value,
            "Turning the status switch off did not update the F1 enabled setting.");
    }

    public static void SceneLifecycle()
    {
        Boot(true);
        var main = new Firebot.Main();
        main.OnSceneWasLoaded(1, "Game");
        Check(BotManager.IsRunning && AutoUpgrade.IsActive && AutoSkill.IsActive, "Ready scene did not start all workers.");
        var upgrade = Handle(typeof(AutoUpgrade), "_autoUpgradeRoutineHandle");
        var skill = Handle(typeof(AutoSkill), "_autoSkillRoutineHandle");
        var hotkeys = Handle(typeof(AutoSkill), "_comboHotkeys");
        main.OnSceneWasLoaded(2, "effects");
        main.OnSceneWasUnloaded(2, "effects");
        Check(ReferenceEquals(upgrade, Handle(typeof(AutoUpgrade), "_autoUpgradeRoutineHandle")),
            "An unrelated additive scene restarted gameplay.");
        main.OnSceneWasUnloaded(1, "Game");
        Check(!AutoUpgrade.IsActive && !AutoSkill.IsActive && !BotManager.IsRunning &&
              !MelonCoroutines.Active.ContainsKey(upgrade) && !MelonCoroutines.Active.ContainsKey(skill),
            "Old-scene workers survived the unload.");
        main.OnSceneWasLoaded(3, "login");
        AutoUpgrade.Update();
        AutoSkill.Update();
        Check(!AutoUpgrade.IsActive && !AutoSkill.IsActive, "Automation restarted outside gameplay.");
        main.OnSceneWasLoaded(1, "Game");
        Check(AutoUpgrade.IsActive && AutoSkill.IsActive && BotManager.IsRunning &&
              !ReferenceEquals(upgrade, Handle(typeof(AutoUpgrade), "_autoUpgradeRoutineHandle")) &&
              !ReferenceEquals(hotkeys, Handle(typeof(AutoSkill), "_comboHotkeys")),
            "Account switch did not rebuild the workers and cached skill targets.");
        var starts = MelonCoroutines.Starts;
        for (var n = 0; n < 5; n++) { AutoUpgrade.Update(); AutoSkill.Update(); }
        Check(starts == MelonCoroutines.Starts, "Enabled workers were duplicated every frame.");

        Input.Pressed = KeyCode.F7;
        main.OnUpdate();
        Input.Pressed = KeyCode.None;
        Entry<bool>("auto_upgrade", "enabled").Value = false;
        Entry<bool>("auto_skill", "enabled").Value = false;
        AutoUpgrade.Update();
        AutoSkill.Update();
        main.OnSceneWasUnloaded(1, "Game");
        main.OnSceneWasLoaded(1, "Game");
        Check(!AutoUpgrade.IsActive && !AutoSkill.IsActive && !BotManager.IsRunning,
            "Account switching undid manual off choices.");
    }

    public static void ToggleState()
    {
        Boot(false);
        AutoUpgrade.OnSceneChanged(true);
        AutoSkill.OnSceneChanged(true);
        Input.Pressed = KeyCode.F6;
        AutoUpgrade.Update();
        Input.Pressed = KeyCode.F8;
        AutoSkill.Update();
        Input.Pressed = KeyCode.None;
        Check(AutoUpgrade.IsActive && AutoSkill.IsActive &&
              Entry<bool>("auto_upgrade", "enabled").Value && Entry<bool>("auto_skill", "enabled").Value,
            "Hotkeys did not enable the displayed settings.");
        HotkeyGate.IsCapturing = true;
        Input.Pressed = KeyCode.F6;
        AutoUpgrade.Update();
        Check(AutoUpgrade.IsActive, "Recording a shortcut toggled automation.");
        Input.Pressed = KeyCode.None;
        Entry<bool>("auto_upgrade", "enabled").Value = false;
        Entry<bool>("auto_skill", "enabled").Value = false;
        AutoUpgrade.Update();
        AutoSkill.Update();
        Check(!AutoUpgrade.IsActive && !AutoSkill.IsActive, "F1 off did not stop the workers.");
        var starts = MelonCoroutines.Starts;
        AutoUpgrade.Update();
        Check(starts == MelonCoroutines.Starts, "Disabled frames repeatedly started menu cleanup.");
        Entry<KeyCode>("auto_upgrade", "shortcut_key").Value = KeyCode.None;
        Entry<bool>("auto_upgrade", "enabled").Value = true;
        Entry<bool>("auto_skill", "enabled").Value = true;
        AutoUpgrade.Update();
        AutoSkill.Update();
        Check(AutoUpgrade.IsActive && AutoSkill.IsActive, "F1 on did not start workers while key capture was active.");
        Entry<string>("auto_skill", "combo_sequence").Value = "";
        AutoSkill.Update();
        AutoSkill.Update();
        Check(!AutoSkill.IsActive && !Entry<bool>("auto_skill", "enabled").Value,
            "An empty combo stayed enabled or restarted endlessly.");
        Entry<string>("auto_skill", "combo_sequence").Value = "2,1";
        Entry<bool>("auto_skill", "enabled").Value = true;
        AutoSkill.Update();
        Check(AutoSkill.IsActive, "A corrected combo could not be re-enabled.");
        AutoUpgrade.OnSceneChanged(false);
        AutoSkill.OnSceneChanged(false);
    }

    public static void UpgradeTaskWindows()
    {
        Boot(true);
        const string list = Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.UpgradesList;
        GameObject.CreatePath(list + "/hero/lvlUpButton").AddComponent<Button>();
        Entry<string>("auto_upgrade", "upgrade_target_slots").Value = "";
        Firebot.GameModel.Features.MainScene.Upgrades.IsVisible = true;
        BotManager.TaskApproaching = true;

        try
        {
            void CheckWindow(string strategy, bool ready, bool executing, bool shouldBuy)
            {
                AutoUpgrade.OnSceneChanged(false);
                Entry<string>("auto_upgrade", "priority_mode").Value = strategy;
                BotManager.PauseAutoUpgrade = ready;
                BotManager.IsTaskExecuting = executing;
                ExecuteEvents.Events.Clear();
                AutoUpgrade.OnSceneChanged(true);
                var worker = MelonCoroutines.Active[Handle(typeof(AutoUpgrade), "_autoUpgradeRoutineHandle")];
                Check(worker.MoveNext(), "The upgrade worker ended instead of waiting or buying.");
                Check(ExecuteEvents.Events.Contains("down") == shouldBuy,
                    $"{strategy}: wrong upgrade decision with ready={ready}, executing={executing}.");
            }

            CheckWindow("balanced", false, false, true);
            CheckWindow("balanced", false, false, true);
            CheckWindow("balanced", true, false, false);
            CheckWindow("balanced", false, true, false);
            CheckWindow("focus", false, false, false);
            CheckWindow("invalid", false, false, false);
            BotManager.TaskApproaching = false;
            CheckWindow("focus", false, false, true);
        }
        finally
        {
            AutoUpgrade.OnSceneChanged(false);
            BotManager.PauseAutoUpgrade = false;
            BotManager.TaskApproaching = false;
            BotManager.IsTaskExecuting = false;
            ExecuteEvents.OnDown = null;
        }
    }

    public static void NestedGuard()
    {
        var failures = 0;
        var disposed = 0;
        var continued = false;
        var wait = new WaitForSeconds(1);
        IEnumerator Child()
        {
            try { yield return wait; throw new InvalidOperationException("nested"); }
            finally { disposed++; }
        }
        IEnumerator Parent()
        {
            try { yield return Child(); continued = true; }
            finally { disposed++; }
        }
        var guard = CoroutineGuard.Run(Parent(), "test", () => failures++);
        Check(guard.MoveNext() && ReferenceEquals(wait, guard.Current), "Unity wait instructions were changed.");
        Check(!guard.MoveNext() && failures == 1 && disposed == 2 && !continued,
            "Nested failure did not stop and dispose both iterators exactly once.");
        guard = CoroutineGuard.Run(new BadCurrent(), "test", () => failures++);
        Check(!guard.MoveNext() && failures == 2, "Current getter exception escaped the guard.");
        guard = CoroutineGuard.Run(null, "test", () => failures++);
        Check(!guard.MoveNext() && failures == 3, "Null routine did not invoke the failure callback.");
        guard = CoroutineGuard.Run(Parent(), "test", () => failures++);
        Check(guard.MoveNext(), "Cancellation fixture did not yield.");
        ((IDisposable)guard).Dispose();
        Check(disposed == 4 && failures == 3, "Cancellation skipped nested finally blocks or reported a false error.");
    }

    public static void WorkerFailures()
    {
        Boot(true);
        const string list = Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.UpgradesList;
        GameObject.CreatePath(list + "/hero/lvlUpButton").AddComponent<Button>();
        ExecuteEvents.Events.Clear();
        AutoUpgrade.OnSceneChanged(true);
        var worker = MelonCoroutines.Active[Handle(typeof(AutoUpgrade), "_autoUpgradeRoutineHandle")];
        Check(worker.MoveNext() && ExecuteEvents.Events.Contains("down"), "Hold fixture did not press the button.");
        AutoUpgrade.OnSceneChanged(false);
        Check(ExecuteEvents.Events.SequenceEqual(new[] { "down", "up" }), "Scene unload left a pointer held or bought on cancellation.");

        ExecuteEvents.OnDown = _ => throw new InvalidOperationException("nested input failure");
        AutoUpgrade.OnSceneChanged(true);
        worker = MelonCoroutines.Active[Handle(typeof(AutoUpgrade), "_autoUpgradeRoutineHandle")];
        Check(!worker.MoveNext() && !AutoUpgrade.IsActive && !Entry<bool>("auto_upgrade", "enabled").Value,
            "Nested hold failure left AutoUpgrade falsely on.");
        var starts = MelonCoroutines.Starts;
        AutoUpgrade.Update();
        Check(starts == MelonCoroutines.Starts, "A failed worker retried every frame.");
        ExecuteEvents.OnDown = null;
        Entry<bool>("auto_upgrade", "enabled").Value = true;
        AutoUpgrade.Update();
        Check(AutoUpgrade.IsActive, "One F1 enable did not recover AutoUpgrade.");

        AutoSkill.OnSceneChanged(true);
        worker = MelonCoroutines.Active[Handle(typeof(AutoSkill), "_autoSkillRoutineHandle")];
        GameObject.BeforeFind = () => throw new InvalidOperationException("nested skill lookup failure");
        Check(!worker.MoveNext() && !AutoSkill.IsActive && !Entry<bool>("auto_skill", "enabled").Value,
            "Nested skill failure left AutoSkill falsely on.");
        GameObject.BeforeFind = null;
        AutoUpgrade.OnSceneChanged(false);
        AutoSkill.OnSceneChanged(false);
    }

    private sealed class LaunchTask : BotTask
    {
        public void Disable() => IsEnabled = false;
        protected override void OnConfigure(MelonPreferences_Category category)
        {
            category.CreateEntry("reserve", 0, "Reserve", "");
            category.CreateEntry("optional_spending", false, "Optional spending", "");
            category.CreateEntry("priority", "damage,health", "Priority", "");
        }
        public override IEnumerator Execute() { yield break; }
    }

    private sealed class BadCurrent : IEnumerator
    {
        public object Current => throw new InvalidOperationException("Current failed");
        public bool MoveNext() => true;
        public void Reset() => throw new NotSupportedException();
    }
}
