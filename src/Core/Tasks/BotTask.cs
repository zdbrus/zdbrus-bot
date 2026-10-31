using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Firebot.GameModel.Base;
using MelonLoader;
using static Firebot.Utilities.StringUtils;

namespace Firebot.Core.Tasks;

public abstract class BotTask
{
    private readonly string _className;
    private MelonPreferences_Category _category;
    private MelonPreferences_Entry<bool> _enabledEntry;
    private GameElement _notificationElement;
    private bool _levelLockLogged;
    private bool _levelWaitLogged;
    private bool _unscheduledRunReported;

    protected BotTask()
    {
        _className = GetType().Name;
    }

    public string SectionTitle => Humanize(GetType().Name);

    public DateTime NextRunTime { get; protected set; } = DateTime.MinValue;

    /// <summary>
    ///     Character level the feature behind this task needs. <see cref="LevelRequirements.None" /> — the
    ///     default — means the feature is available from the start, and costs no game lookup.
    /// </summary>
    public virtual int MinimumLevel => LevelRequirements.None;

    public DateTime? LastRunTime { get; set; }

    /// <summary>Puts back a due time saved before the game was closed.</summary>
    public void RestoreNextRun(DateTime when) => NextRunTime = when;

    protected virtual string NotificationPath => null;

    /// <summary>True when a task must revisit its screen at its calculated timer, even without a bubble.</summary>
    protected virtual bool UseScheduledTime => false;

    /// <summary>Higher values run first when more than one task is ready.</summary>
    internal virtual int SchedulerPriority => 0;

    internal virtual IEnumerable<string> HandledNotificationNames
    {
        get
        {
            var path = NotificationPath;
            return string.IsNullOrEmpty(path)
                ? Array.Empty<string>()
                : new[] { path[(path.LastIndexOf('/') + 1)..] };
        }
    }

    public bool IsEnabled
    {
        get => _enabledEntry?.Value ?? false;
        protected set => _enabledEntry.Value = value;
    }

    private GameElement NotificationElement
    {
        get
        {
            if (_notificationElement != null) return _notificationElement;
            if (string.IsNullOrEmpty(NotificationPath)) return null;

            _notificationElement = new GameElement(NotificationPath);
            return _notificationElement;
        }
    }

    public void InitializeConfig(string configPath)
    {
        if (_enabledEntry != null) return;

        var sectionId = SectionTitle.Replace(" ", "_").ToLowerInvariant();

        // The display name is what the panel shows; the .cfg stores only the identifier. So it carries no
        // "Settings" suffix — the window is already the bot's settings, and the section header is what
        // names the feature.
        _category = MelonPreferences.CreateCategory(sectionId, SectionTitle);
        _category.SetFilePath(configPath);

        // Named after the task rather than a generic "Enable Task": the row can then be read on its own,
        // without the section header above it.
        _enabledEntry = _category.CreateEntry("enabled", EnabledByDefault, $"Enable {SectionTitle}",
            $"Enables or disables the {SectionTitle} automation task." +
            $"\nWhen disabled, this task will be ignored during the execution loop." +
            LevelRequirementNote);

        if (BotSettings.EnableAllOnLaunch) _enabledEntry.Value = true;
        OnConfigure(_category);
        _category.SaveToFile();
    }

    /// <summary>On for a task that only spends free, earned resources, so it works without visiting the panel.</summary>
    protected virtual bool EnabledByDefault => false;

    protected virtual void OnConfigure(MelonPreferences_Category category) { }

    /// <summary>
    ///     True while the game does not offer this task's feature yet, or while the level is not known.
    ///     <para>
    ///         The unknown case is a lock on purpose, but only for a task that asked for a level: running the
    ///         task would click a menu that may not exist yet, which is what breaks the flow. A task with no
    ///         requirement returns on the first line and never looks at the level.
    ///     </para>
    ///     <para>
    ///         Nothing is written back to the settings. The user's "enabled" is theirs; this is a runtime
    ///         condition, and it clears itself the moment the level arrives — including for the level
    ///         requirement the character has not reached yet.
    ///     </para>
    /// </summary>
    public bool IsLevelLocked
    {
        get
        {
            if (MinimumLevel <= LevelRequirements.None) return false;

            if (!PlayerStats.TryGetCharacterLevel(out var level))
            {
                if (!_levelWaitLogged)
                {
                    _levelWaitLogged = true;
                    Debug($"Level not read yet; holding off (needs {MinimumLevel}).");
                }

                return true;
            }

            _levelWaitLogged = false;

            if (level >= MinimumLevel)
            {
                _levelLockLogged = false;
                return false;
            }

            if (!_levelLockLogged)
            {
                _levelLockLogged = true;
                Logger.Debug($"[{SectionTitle}] Locked until level {MinimumLevel}. Character is at {level}.");
            }

            return true;
        }
    }

    /// <summary>
    ///     One more line on the task's own toggle when the game only offers the feature later, and nothing at all
    ///     for a task that is available from the start. The panel shows this text as the row's description, so the
    ///     requirement sits exactly where the user turns the task on — and it must stay inside the three lines
    ///     that box holds.
    /// </summary>
    private string LevelRequirementNote => MinimumLevel <= LevelRequirements.None
        ? string.Empty
        : $"\nHeld back until the character is level {MinimumLevel}.";

    /// <summary>
    ///     Bubble tasks normally wait for a notification, with a six-hour safety check. Tasks that need a
    ///     precise timer can opt in to running at their scheduled time as well.
    /// </summary>
    public bool IsReady()
        => !IsLevelLocked && (IsNotificationVisibleCore() || (IsEnabled && DateTime.Now >= DueTime));

    /// <summary>Checks readiness when the caller has already checked notification visibility.</summary>
    public bool IsReady(bool notificationVisible)
        => !IsLevelLocked && (notificationVisible || (IsEnabled && DateTime.Now >= DueTime));

    /// <summary>When the timer next runs this task, independent of an earlier notification.</summary>
    public DateTime DueTime => DueAt(NextRunTime);

    internal DateTime DueAt(DateTime scheduled)
    {
        if (scheduled == DateTime.MinValue || string.IsNullOrEmpty(NotificationPath) || UseScheduledTime)
            return scheduled;

        return scheduled > DateTime.MaxValue.AddHours(-6) ? DateTime.MaxValue : scheduled.AddHours(6);
    }

    /// <summary>
    ///     The check the scheduler makes before it even looks at readiness: a popup can be clicked from here,
    ///     so the level guard has to be in front of it too.
    /// </summary>
    public bool IsNotificationVisible()
        => !IsLevelLocked && IsNotificationVisibleCore();

    /// <summary>The level is looked up once per call through the guards above, not once per branch.</summary>
    protected virtual bool IsNotificationVisibleCore()
        => IsEnabled && DateTime.Now >= BubbleQuietUntil && NotificationElement != null && NotificationElement.IsVisible();

    /// <summary>
    ///     Until this time the task's bubble does not trigger a run (its timer still does). Keeps a bubble that stays lit,
    ///     because there is nothing affordable to do, from taking every scan and starving the other tasks.
    /// </summary>
    public DateTime BubbleQuietUntil { get; set; } = DateTime.MinValue;

    /// <summary>Consecutive bubble-triggered runs after which the bubble was still lit. Drives the backoff.</summary>
    public int BubbleIdleStreak { get; set; }

    public bool IsBubbleStillLit() => IsNotificationVisibleCore();

    public abstract IEnumerator Execute();

    /// <summary>Releases native actions that can outlive a stopped coroutine.</summary>
    internal virtual void OnStopped() { }

    /// <summary>
    ///     Called by the manager after a run. A task that returns without scheduling itself leaves
    ///     <see cref="NextRunTime" /> at <see cref="DateTime.MinValue" /> — the earliest value there is — so from
    ///     then on it would win the scan every time and the other tasks would never get a turn, with nothing in
    ///     the log to explain why. The time is nudged forward here instead, and the mistake reported once.
    /// </summary>
    public void EnsureScheduled()
    {
        // A time already in the past (a timer that just ran out) is retried shortly, not on every scan.
        if (NextRunTime != DateTime.MinValue)
        {
            if (NextRunTime < DateTime.Now.AddSeconds(20)) NextRunTime = DateTime.Now.AddSeconds(20);
            return;
        }

        // Nothing to schedule from (no timer on screen, e.g. no expeditions left today): look again in 15 minutes.
        // The task's notification bubble still brings it back sooner when something becomes available.
        var backoff = 15 * 60;
        NextRunTime = DateTime.Now.AddSeconds(backoff);

        if (_unscheduledRunReported) return;

        _unscheduledRunReported = true;
        Logger.Debug($"{SectionTitle} had no timer to schedule from; checking again in {backoff / 60:0} minutes.");
    }

    protected void Debug(string message, [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => Logger.Debug($"[{_className}::{member}:{line}] {message}");
}