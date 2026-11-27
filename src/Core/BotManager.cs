using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Firebot.BotActions;
using Firebot.Core.Tasks;
using Firebot.Utilities;
using MelonLoader;
using UnityEngine;
using static Firebot.Core.BotSettings;

namespace Firebot.Core;

public static class BotManager
{
    private const double AutoUpgradeMinExecutionWindowSeconds = 30d;

    /// <summary>
    ///     Slack over the configured scan interval before the scheduler counts as not having woken up. A frame
    ///     can be late, and a wait is rounded up to one, but a scan half a minute past its due time is a stopped
    ///     clock rather than a slow frame.
    /// </summary>
    private const double ScanWakeGraceSeconds = 30d;

    private static readonly List<BotTask> Tasks = new();
    private static object _botRoutineHandle;
    private static bool _shouldPauseAutoUpgrade;
    private static BotTask _executingTask;
    private static DateTime _lastStepAt;

    /// <summary>
    ///     When the loop's sleep is due to end, or <see cref="DateTime.MaxValue" /> while it is not asleep — the
    ///     value it also starts at, before the first scan.
    /// </summary>
    private static DateTime _nextScanDueAt = DateTime.MaxValue;

    private static bool _scanWakeWarned;
    public static bool IsRunning { get; private set; }

    internal static IReadOnlyList<BotTask> AllTasks => Tasks;
    public static bool IsTaskExecuting { get; private set; }

    /// <summary>True while a task is in the middle of running (the planner waits for this before it clicks).</summary>
    public static bool IsBusy => IsRunning && IsTaskExecuting;

    internal static bool SetTaskEnabled(BotTask task, bool enabled)
    {
        if (task == null || !Tasks.Contains(task) || !task.SetEnabledFromStatus(enabled)) return false;

        MelonPreferences.Save();
        Logger.Info($"[Bot] {task.SectionTitle} turned {(enabled ? "on" : "off")} from F2.");
        return true;
    }

    /// <summary>The first enabled, unlocked task the player asked to run from F2, or null.</summary>
    internal static BotTask PickRunNow(IEnumerable<BotTask> tasks)
    {
        foreach (var task in tasks)
            if (task.RunNowRequested)
            {
                if (task.IsEnabled && !task.IsLocked) return task;
                task.RunNowRequested = false;
            }
        return null;
    }

    /// <summary>Queues an enabled task to run at the scheduler's next pick, ignoring its timer (F2 "Run").</summary>
    internal static bool RunTaskNow(BotTask task)
    {
        if (task == null || !Tasks.Contains(task) || !task.IsEnabled) return false;
        task.RequestRunNow();
        Logger.Info($"[Bot] {task.SectionTitle} queued to run now from F2.");
        if (!IsRunning)
        {
            Logger.Info("[Bot] Bot was stopped; starting it so the task runs.");
            Start();
        }
        return true;
    }

    public static bool ShouldPauseAutoUpgrade() => IsRunning && (_shouldPauseAutoUpgrade || IsTaskExecuting);

    /// <summary>Why AutoUpgrade is paused right now, for its long-pause warning.</summary>
    public static string AutoUpgradePauseReason =>
        IsTaskExecuting ? $"{_executingTask?.SectionTitle ?? "a task"} is running" : _autoUpgradePauseReason;

    private static string _autoUpgradePauseReason = "";

    /// <summary>The last scan found the game's tutorial running, and held every task back for it.</summary>
    private static bool _tutorialHold;
    private static bool _playerWaitLogged;

    /// <summary>The player has used the game within the quiet window (refreshed every scan). AutoUpgrade reads it too.</summary>
    public static bool PlayerBusy { get; private set; }

    /// <summary>A screen is open that the bot did not leave open itself (the player's own).</summary>
    public static bool PlayerScreenOpen { get; private set; }

    private static DateTime _lastWatchTick = DateTime.MinValue;

    /// <summary>
    ///     Longest gap between two game updates that still counts as the bot being alive. A longer one means the
    ///     whole game stopped updating (minimised, a hitch while it talks to the server, the PC asleep), and the
    ///     time the stall watch measures in that gap is the game's, not the task's.
    /// </summary>
    private const double GameFrozenGapSeconds = 15;

    /// <summary>
    ///     Stops the bot when the task it is running has stopped advancing, and reports a scan that never came
    ///     back.
    ///     <para>
    ///         <see cref="RunSafe" /> times out between the steps of the routine it drives, so a task that
    ///         never comes back from a step it handed to Unity — a nested coroutine looping on itself, such
    ///         as clicking a button that never stops being clickable — is invisible to it: the timeout is
    ///         never reached, the scan never returns, no other task ever runs, and the auto upgrade stays
    ///         paused because a task is still marked as executing. Nothing in the log says why.
    ///     </para>
    ///     <para>
    ///         This runs from <c>OnUpdate</c> instead of from a coroutine, which is what lets it see that
    ///         case at all, and it keeps ticking while every wait in the mod is stuck on scaled time.
    ///         Stopping is the way out: it drops the scheduler, and the nested routine goes with it. A
    ///         legitimate execution never trips it — the longest single wait in the mod is
    ///         <c>InteractionDelay</c>, a few seconds, against a limit measured in minutes.
    ///     </para>
    /// </summary>
    public static void WatchForStall()
    {
        var tick = DateTime.Now;
        var gap = _lastWatchTick == DateTime.MinValue ? TimeSpan.Zero : tick - _lastWatchTick;
        _lastWatchTick = tick;
        if (!IsRunning) return;

        if (gap.TotalSeconds > GameFrozenGapSeconds)
        {
            // Nothing in the mod could have advanced while the game was not updating: start the stall clock over
            // instead of stopping the bot for a pause it did not cause (issue #10: "has not advanced for 511s").
            _lastStepAt = tick;
            if (IsTaskExecuting)
                Logger.Warning($"[Bot] The game did not update for {gap.TotalSeconds:0}s (minimised, frozen or the PC " +
                               "slept). The task carries on.");
            return;
        }

        if (IsTaskExecuting)
        {
            var stalledFor = DateTime.Now - _lastStepAt;
            if (stalledFor <= TimeSpan.FromSeconds(MaxTaskRuntime)) return;

            var task = _executingTask?.SectionTitle ?? "A task";
            Logger.Error($"[FAILED] {task} has not advanced for {stalledFor.TotalSeconds:0}s " +
                         $"(limit {MaxTaskRuntime:0}s). Stopping the bot.");
            Stop();
            return;
        }

        // While the loop is asleep, a scan that never comes back is the game clock stopped rather than the bot
        // stuck: the wait is scaled time, so timeScale == 0 never resumes it, and the mod goes quiet while the
        // bot still reads as running. Warned and not stopped on purpose — this one heals itself when the clock
        // returns, and stopping would end a session the user did not ask to end. Once per sleep, so it
        // explains the silence without filling the log with it.
        var lateBy = DateTime.Now - _nextScanDueAt;
        if (lateBy <= TimeSpan.FromSeconds(ScanWakeGraceSeconds) || _scanWakeWarned) return;

        _scanWakeWarned = true;
        Logger.Warning($"[Bot] The scan has not resumed for {lateBy.TotalSeconds:0}s " +
                       $"(interval {ScanInterval:0}s). Nothing is scheduled until it does — most likely the " +
                       "game clock is stopped.");
    }

    public static void Initialize()
    {
        const string targetNamespace = "Firebot.Behaviors";
        Tasks.Clear();

        var assembly = Assembly.GetExecutingAssembly();
        var taskTypes = assembly.GetTypes()
            .Where(task => task.Namespace != null && task.Namespace.StartsWith(targetNamespace) &&
                           task.IsSubclassOf(typeof(BotTask)) && !task.IsAbstract);

        foreach (var type in taskTypes)
            try
            {
                var task = (BotTask)Activator.CreateInstance(type);
                if (task != null)
                {
                    task.InitializeConfig(ConfigPath);
                    Tasks.Add(task);
                }
            }
            catch (Exception e)
            {
                Logger.Info($"[Loader] Failed to load {type.Name}: {e.GetType().Name} - {e.Message}");
            }

        ScheduleStore.Restore(Tasks);
    }

    public static void Start()
    {
        if (IsRunning) return;
        if (Tasks.Count == 0) Initialize();

        IsRunning = true;
        _shouldPauseAutoUpgrade = false;

        // The loop has not slept yet. A due time left over from a previous run would read as a scan that never
        // came back — during the auto start delay, which is not a scan wait at all.
        _nextScanDueAt = DateTime.MaxValue;
        _scanWakeWarned = false;

        _botRoutineHandle = MelonCoroutines.Start(BotSchedulerLoop());
        Logger.Info($"Started. Tasks loaded: {Tasks.Count(t => t.IsEnabled)}");
    }

    public static void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        IsTaskExecuting = false;
        _shouldPauseAutoUpgrade = false;

        // Stopping a coroutine does not run its finally, so the task pinned to the top of the table has to be
        // released here too.
        ReleaseTask(_executingTask);
        _executingTask = null;
        if (_botRoutineHandle != null) MelonCoroutines.Stop(_botRoutineHandle);
        _botRoutineHandle = null;
        Logger.Info("Stopped.");
    }

    private static IEnumerator BotSchedulerLoop()
    {
        if (AutoStart) yield return new WaitForSeconds(StartBotDelay);
        var retainedHub = false;

        while (IsRunning)
        {
            // Awake, so a pause already reported can be reported again on the next one.
            _scanWakeWarned = false;

            BotTask notificationTask = null;
            BotTask readyTask = null;
            var ranTask = false;

            // The scan is the one part of the loop nothing else guards. What a task throws is caught by RunSafe,
            // but an exception here — a game lookup failing while the UI is mid-rebuild, say — would end the
            // coroutine and, with it, the session: the bot would read as running with nothing ever scheduled
            // again, which is the same silence the stall checks exist to break. It is also the synchronous
            // part, and that is what makes it catchable: yield return is not allowed in a try that has a catch.
            try
            {
                var earliest = DateTime.MaxValue;
                var nextEnabledTaskRun = DateTime.MaxValue;
                BotTask nearestTask = null;

                foreach (var task in Tasks)
                {
                    // A task locked by level is enabled but cannot run. Counting its next run — MinValue, for
                    // one that never ran — would leave the auto upgrade thinking a task is always about to
                    // fire, and it would never resume while the lock lasts.
                    // DueTime, not NextRunTime: a bubble task waiting on its bubble has a next run time in the past,
                    // which read as "a task is about to run" forever and kept auto-upgrade paused (0.7.6 bug).
                    if (task.IsEnabled && !task.IsLocked && task.DueTime < nextEnabledTaskRun)
                    {
                        nextEnabledTaskRun = task.DueTime;
                        nearestTask = task;
                    }

                    var notificationVisible = task.IsNotificationVisible();
                    if (notificationVisible &&
                        (notificationTask == null || task.SchedulerPriority > notificationTask.SchedulerPriority))
                        notificationTask = task;

                    if (!task.IsReady(notificationVisible)) continue;
                    if (readyTask != null &&
                        (task.SchedulerPriority < readyTask.SchedulerPriority ||
                         (task.SchedulerPriority == readyTask.SchedulerPriority && task.DueTime >= earliest)))
                        continue;

                    earliest = task.DueTime;
                    readyTask = task;
                }

                if (notificationTask != null &&
                    (readyTask == null || notificationTask.SchedulerPriority >= readyTask.SchedulerPriority))
                    readyTask = notificationTask;

                // F2 RUN goes first: lit bubbles and priorities would otherwise keep pushing it back.
                var requested = PickRunNow(Tasks);
                if (requested != null) readyTask = requested;

                var hasNearTask = nextEnabledTaskRun != DateTime.MaxValue &&
                                  (nextEnabledTaskRun - DateTime.Now).TotalSeconds <=
                                  AutoUpgradeMinExecutionWindowSeconds;
                _shouldPauseAutoUpgrade = AutoUpgrade.ShouldWaitForTask(
                    notificationTask != null || readyTask != null, hasNearTask);
                _autoUpgradePauseReason = readyTask != null && readyTask == notificationTask
                    ? $"{readyTask.SectionTitle} bubble is lit"
                    : readyTask != null ? $"{readyTask.SectionTitle} is ready"
                    : _shouldPauseAutoUpgrade && hasNearTask
                        ? $"{nearestTask?.SectionTitle ?? "a task"} is due at {nextEnabledTaskRun:HH:mm:ss}"
                    : "";
            }
            catch (Exception e)
            {
                Logger.Error($"[FAILED] The scan threw: {e.GetType().Name} - {e.Message}. Stopping the bot.");

                // Cleared here rather than through Stop(): that stops this very coroutine from the inside. Ending
                // the loop and lowering the flag is the same state, reached without re-entering MelonCoroutines.
                IsRunning = false;
                yield break;
            }

            // The planner is applying a squad: let it finish its clicks before the next job starts.
            var plannerBusy = Planner.SquadApplier.Busy || Planner.JewelSpender.Busy || Planner.SoulShardSpender.Busy || Planner.VoidCrystalSpender.Busy;
            if (plannerBusy) readyTask = null;

            // A new account's tutorial step waits for the player to press one highlighted thing. A task opening
            // its own menu on top of that either clicks nothing or knocks the step out of order, so nothing runs
            // until the step is done. Read here, outside the scan's try: the read guards itself.
            _tutorialHold = GameTutorial.ShouldHold;
            if (_tutorialHold) readyTask = null;

            // The player is using the game (Tree of Life, a hero, the shop, scrolling the map): leave it alone until
            // they have been quiet for a moment, instead of sweeping their screen shut for the next task. Any
            // input counts, not only input with a menu up: closing the map or a battle view under a player who is
            // clicking it was the other half of "the bot interrupts everything I do" (issue #8).
            // The hub screen a task leaves open on purpose is the bot's, not the player's: it must not count as
            // "a screen you opened", or the tasks and the cleanup after it wait three times as long for nothing.
            var botUpgrades = BotActions.AutoUpgrade.OwnsMenu;
            var playerScreenOpen = !retainedHub &&
                                   Behaviors.Extra.Ui.VisibleChildren(Behaviors.Extra.P.Menus)
                                       .Any(c => !(botUpgrades && c.name == "Upgrades"));
            var playerBusy = UserActivityPolicy.ShouldWait(UserActivity.SecondsSinceInput,
                playerScreenOpen, false, PlayerQuietSeconds);
            PlayerBusy = playerBusy;
            PlayerScreenOpen = playerScreenOpen;
            // Leaving the hub the bot itself left open only needs a short pause in the player's input (3 seconds).
            var cleanupBusy = UserActivityPolicy.ShouldWait(UserActivity.SecondsSinceInput, false, false,
                Math.Min(PlayerQuietSeconds, 3f));
            // A task that never closes the player's screen (Boss Retry only presses "Boss") has nothing to wait for.
            if (readyTask != null && !IsTaskExecuting && readyTask.NeedsCleanScreen &&
                UserActivityPolicy.ShouldWait(UserActivity.SecondsSinceInput,
                    playerScreenOpen && !Behaviors.Extra.Ui.MenuOpenFor(readyTask.SectionTitle),
                    readyTask.RunNowRequested, PlayerQuietSeconds))
            {
                if (!_playerWaitLogged)
                {
                    _playerWaitLogged = true;
                    Logger.Debug($"[Bot] {readyTask.SectionTitle} is due; waiting while you use the game.");
                }

                readyTask = null;
            }
            else if (readyTask != null) _playerWaitLogged = false;

            // Story dialogue, "New hero!" and the chapter reward sit on top of everything and have no close button
            // for the sweep. Cleared between tasks too, but a new account can go a long time with no task due.
            if (!plannerBusy && Watchdog.StoryPopupOpen && !GameTutorial.IsForced)
            {
                IsTaskExecuting = true;
                _lastStepAt = DateTime.Now;
                try { yield return RunSafe(Watchdog.DismissStory(), "Story popups"); }
                finally { IsTaskExecuting = false; }

                // The click that closed the story is often the one that starts the next tutorial step.
                _tutorialHold = GameTutorial.ShouldHold;
                if (_tutorialHold) readyTask = null;
            }

            // The idle cleanup closes whatever is open to get back to the upgrade screen. When the player has been
            // using the game since the last task, what is open is theirs: keep the hub flag and try again later.
            if (readyTask == null && retainedHub && !_tutorialHold && !plannerBusy && !cleanupBusy)
            {
                var pauseForNearTask = _shouldPauseAutoUpgrade;
                _shouldPauseAutoUpgrade = true;
                IsTaskExecuting = true;
                // The stall watch measures from the last step. After a long idle stretch that was the previous task,
                // minutes ago, and the watch stopped the bot before the cleanup took its first step (issue #10:
                // "A task has not advanced for 511s", right after the last task finished).
                _lastStepAt = DateTime.Now;
                try { yield return RunSafe(Watchdog.ForceClearAll("idle: return for upgrades"), "Idle cleanup"); }
                finally
                {
                    IsTaskExecuting = false;
                    _shouldPauseAutoUpgrade = pauseForNearTask;
                    retainedHub = false;
                }
            }

            // A task that can tell without opening anything that it has nothing to do is settled here, before the
            // cleanup that would close the player's screen for it.
            if (readyTask != null && !readyTask.RunNowRequested)
            {
                var skipped = false;
                try { skipped = readyTask.SkipWithoutScreen(); }
                catch (Exception e)
                {
                    Logger.Error($"[FAILED] {readyTask.SectionTitle} quick check threw: {e.GetType().Name} - {e.Message}");
                }

                if (skipped)
                {
                    readyTask.LastRunTime = DateTime.Now;
                    readyTask.EnsureScheduled();
                    ScheduleStore.Save(Tasks);
                    readyTask = null;
                }
            }

            if (readyTask != null)
            {
                if (AutoUpgrade.IsBalancedCycleActive)
                {
                    yield return AutoUpgrade.WaitForBalancedCycle();
                    continue;
                }

                IsTaskExecuting = true;
                _executingTask = readyTask;
                _lastStepAt = DateTime.Now;
                ranTask = true;
                try
                {
                    // Already on this task's screen (the player or the last task left it open): keep it and let the
                    // task carry on from there, instead of closing everything and walking back from town.
                    var cleanScreen = readyTask.NeedsCleanScreen;
                    if (!cleanScreen)
                    {
                        // Works over whatever is open; the hub kept by the last task stays kept.
                    }
                    else if (Behaviors.Extra.Ui.MenuOpenFor(readyTask.SectionTitle))
                        Logger.Debug($"[Bot] {readyTask.SectionTitle}: already on its screen, starting from there.");
                    else
                        yield return RunSafe(Watchdog.Clear($"before {readyTask.SectionTitle}",
                                TaskNavigation.HubFor(readyTask.GetType().Name)),
                            $"Watchdog cleanup before {readyTask.SectionTitle}");
                    if (cleanScreen) retainedHub = false;

                    // Stopwatch, completion line and table are all diagnostics: with debug off none of
                    // them is needed, and the console stays quiet in normal use. The guard is single for
                    // the whole block — and the stopwatch is only created when it is going to be read,
                    // because Logger.Debug filters the write but not the cost of building the output.
                    var stopwatch = Logger.IsDebugEnabled ? Stopwatch.StartNew() : null;

                    readyTask.RunNowRequested = false;
                    var fromBubble = readyTask == notificationTask;
                    yield return RunSafe(readyTask.Execute(), $"Task {readyTask.SectionTitle}");
                    ReleaseTask(readyTask);
                    readyTask.LastRunTime = DateTime.Now;
                    // A bubble that is still lit right after its task ran (nothing it could do) waits a little
                    // before it can trigger again, so other tasks get their turn.
                    // Each further run that leaves it lit doubles the wait (3, 6, 12, 24, then 60 minutes), so a task that
                    // cannot act is not reopened every few minutes all day. A cleared bubble resets the count.
                    if (fromBubble)
                    {
                        var stillLit = readyTask.IsBubbleStillLit();
                        readyTask.BubbleIdleStreak = stillLit ? readyTask.BubbleIdleStreak + 1 : 0;
                        var wait = Math.Min(60, 3 * (1 << Math.Clamp(readyTask.BubbleIdleStreak - 1, 0, 5)));
                        var until = DateTime.Now.AddMinutes(Math.Max(3, wait));
                        if (readyTask.BubbleQuietUntil < until) readyTask.BubbleQuietUntil = until;
                    }
                    else readyTask.BubbleIdleStreak = 0;

                    // The task owns its schedule, so it also owns the promise of leaving one behind.
                    readyTask.EnsureScheduled();
                    ScheduleStore.Save(Tasks);

                    if (stopwatch != null)
                    {
                        stopwatch.Stop();

                        // Diagnostics are not allowed to end the session either: with debug off none of this
                        // runs, and the console is the one writing the table, not the bot.
                        try
                        {
                            Logger.Info($"[Task] {readyTask.SectionTitle} finished in {stopwatch.Elapsed.TotalSeconds:0.###}s | Next: {readyTask.NextRunTime:dd/MM/yyyy HH:mm:ss}");

                            // The table is already live on the status screen (F2). The blank line is part of it.
                            if (DateTime.Now - _lastTableAt >= TimeSpan.FromMinutes(10))
                            {
                                _lastTableAt = DateTime.Now;
                                Console.WriteLine();
                                PrintTasksStatusTable();
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"[FAILED] Reporting {readyTask.SectionTitle} threw: " +
                                         $"{e.GetType().Name} - {e.Message}");
                        }
                    }

                    if (cleanScreen)
                    {
                        var hub = TaskNavigation.HubFor(readyTask.GetType().Name);
                        yield return RunSafe(Watchdog.Clear($"after {readyTask.SectionTitle}", hub),
                            $"Watchdog cleanup after {readyTask.SectionTitle}");
                        retainedHub = hub != TaskHub.None;
                    }
                }
                finally
                {
                    ReleaseTask(readyTask);
                    IsTaskExecuting = false;
                    _executingTask = null;
                }
            }

            // The wait is scaled time, so this is the one moment where a stopped game clock shows. Armed
            // around the wait alone: WatchForStall reads it as "asleep past its due", and while the loop is
            // working there is no sleep to be late for.
            // Right after a task that left its hub open, look again soon: if nothing else is due the idle cleanup
            // closes the hub then, instead of a full scan later (issue #10: tasks lingered on the town and arena
            // screens for 5 to 20 seconds).
            var scanWait = ranTask && retainedHub ? Math.Min(1f, ScanInterval) : ScanInterval;
            _nextScanDueAt = DateTime.Now + TimeSpan.FromSeconds(scanWait);
            yield return new WaitForSeconds(scanWait);
        }
    }

    private static IEnumerator RunSafe(IEnumerator routine, string context)
    {
        if (routine == null)
        {
            Logger.Info($"[FAILED] {context} returned null routine.");
            yield break;
        }

        var timeoutSeconds = MaxTaskRuntime;
        var timeoutEnabled = timeoutSeconds > 0f;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            object current = null;
            bool movedNext;

            if (timeoutEnabled && stopwatch.Elapsed.TotalSeconds > timeoutSeconds)
            {
                Logger.Info($"[FAILED] {context} timed out after {timeoutSeconds:0.###}s.");
                yield break;
            }

            try
            {
                movedNext = routine.MoveNext();
                if (movedNext) current = routine.Current;
            }
            catch (Exception e)
            {
                Logger.Info($"[FAILED] {context} threw: {e.GetType().Name} - {e.Message}");
                yield break;
            }

            if (!movedNext) yield break;

            // The routine produced a step, so the execution as a whole is alive: WatchForStall reads this.
            // It is the only signal that survives a step which never returns — the timeout above is checked
            // between steps, so that step is exactly the one it cannot see.
            _lastStepAt = DateTime.Now;

            yield return current;
        }
    }

    private static void ReleaseTask(BotTask task)
    {
        try { task?.OnStopped(); }
        catch (Exception e)
        {
            Logger.Error($"[FAILED] Stopping {task?.SectionTitle}: {e.GetType().Name} - {e.Message}");
        }
    }

    /// <summary>
    ///     Prints the status table to the console.
    ///     It must only be called when <see cref="Logger.IsDebugEnabled" /> is true: the lines use
    ///     <c>Info</c> on purpose, to preserve the box alignment instead of prefixing 13 lines with
    ///     [DEBUG]. The caller is what guarantees the gate.
    /// </summary>
    private static DateTime _lastTableAt = DateTime.MinValue;

    private static void PrintTasksStatusTable()
    {
        var now = DateTime.Now;
        Logger.Info($"[Bot Status] Task Table - {now:dd/MM/yyyy HH:mm:ss}");

        // Same column order as the in-game screen, so a log line and a screenshot can be read side by side.
        Logger.Info("| Task                      | Status        | Time Left   | Next Run            | Last Run            |");
        Logger.Info("|---------------------------|---------------|-------------|---------------------|---------------------|");

        // The rows come from the same place the in-game screen consumes; only the console layout is left here.
        foreach (var row in GetStatusRows(now))
            Logger.Info(
                $"| {row.Name,-25} | {row.Status,-13} | {row.TimeLeft,-11} | {FormatConsoleDate(row.NextRun),-19} | {FormatConsoleDate(row.LastRun),-19} |");
    }

    /// <summary>Day first, the same convention the status screen prints.</summary>
    private static string FormatConsoleDate(DateTime? value)
        => value?.ToString("dd/MM/yyyy HH:mm:ss") ?? TaskStatusRow.NoValue;

    /// <summary>
    ///     One row per task: the one being executed first, then the ones with a next run, and last the ones
    ///     that cannot run — disabled, or waiting for a level.
    ///     <paramref name="now" /> is a parameter rather than an internal DateTime.Now so the consumer can
    ///     line up "Time Left" with its own timestamp, instead of each one taking a slightly different instant.
    /// </summary>
    internal static List<TaskStatusRow> GetStatusRows(DateTime now)
    {
        var rows = new List<TaskStatusRow>(Tasks.Count);
        AppendStatusRows(rows, now);
        return rows;
    }

    /// <summary>
    ///     Fills an existing buffer, so consumers that refresh every second (the status screen) do not
    ///     allocate a new list on every refresh.
    /// </summary>
    internal static void AppendStatusRows(List<TaskStatusRow> buffer, DateTime now)
    {
        // Three groups: the task being executed (what the user is watching), the tasks that have a next run,
        // and the ones that cannot run — disabled, or locked until a level. The last group shows nothing but
        // "-", and its NextRunTime is MinValue in every row, so ordering by next run alone put exactly the
        // rows with nothing to say at the top of the table. Name breaks the tie inside a group: MinValue ties
        // would otherwise be decided by the order the tasks happen to be loaded in.
        foreach (var t in Tasks
                     .OrderBy(t => t == _executingTask ? 0 : HasPendingRun(t) ? 1 : 2)
                     .ThenBy(t => t.DueTime)
                     .ThenBy(t => t.SectionTitle))
        {
            var hasPendingRun = HasPendingRun(t) && t.NextRunTime != DateTime.MinValue;
            var due = t.DueTime;

            buffer.Add(new TaskStatusRow(
                t,
                GetTaskStatus(t),
                // A task without a next run has no time left either. Without this the subtraction runs anyway
                // — from DateTime.MinValue for a task that never ran — and the column shows "0s" beside an
                // empty Next Run.
                hasPendingRun ? TimeParser.FormatFriendlyDuration(due - now) : TaskStatusRow.NoValue,
                hasPendingRun ? due : (DateTime?)null,
                t.LastRunTime));
        }
    }

    /// <summary>
    ///     Whether the task has a next run worth showing. Disabled is the user's choice and a lock is the game's;
    ///     in neither case can the task run now, so neither has a next run or a time left.
    /// </summary>
    private static bool HasPendingRun(BotTask t) => t.IsEnabled && !t.IsLocked;

    private static string GetTaskStatus(BotTask t)
    {
        if (!t.IsEnabled) return TaskStatusRow.Disabled;
        if (t.IsLevelLocked) return TaskStatusRow.Locked(t.MinimumLevel);
        if (t.IsFeatureLocked) return TaskStatusRow.Locked(GameUnlocks.ShortName(t.RequiredFeature));

        var status = t.IsNotificationVisible() ? TaskStatusRow.Popup
            : t.IsReady() ? TaskStatusRow.Ready
            : TaskStatusRow.Waiting;

        // Would run, but the tutorial has the screen. Waiting stays Waiting: nothing is being held back yet.
        return IsRunning && _tutorialHold && status != TaskStatusRow.Waiting ? TaskStatusRow.Tutorial : status;
    }
}