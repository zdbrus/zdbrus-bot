using System;
using System.Collections;
using System.Collections.Generic;
using Firebot.Core;
using Firebot.GameModel.Base;
using Firebot.GameModel.Features.MainScene;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;
using Firebot.Utilities;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using Logger = Firebot.Core.Logger;

namespace Firebot.BotActions;

public static class AutoUpgrade
{
    private const float HoldPerButtonSeconds = 0.5f;
    private const float GapBetweenButtonsSeconds = 0.5f;
    private const float IdlePollSeconds = 0.5f;
    private const int FocusBurstLimit = 20;

    private static readonly WaitForSeconds GapBetweenButtonsWait = new(GapBetweenButtonsSeconds);
    private static readonly WaitForSeconds IdlePollWait = new(IdlePollSeconds);
    private static readonly List<CachedGameButton> ButtonsBuffer = new();
    private static CachedGameButton _activeHold;
    private static bool _isRunning;
    private static object _autoUpgradeRoutineHandle;
    private static object _closeMenuHandle;
    private static bool _sceneReady;
    private static bool _isInitialized;
    private static bool _balancedSweepActive;
    private static MelonPreferences_Entry<KeyCode> _shortcutKey;
    private static MelonPreferences_Entry<bool> _isEnabled;
    private static MelonPreferences_Entry<string> _upgradeTargetSlots;
    private static MelonPreferences_Entry<string> _upgradeMode;
    private static MelonPreferences_Entry<string> _priorityMode;
    private static MelonPreferences_Entry<int> _everySeconds;

    private static bool IsEnabled => _isEnabled?.Value ?? false;

    /// <summary>
    ///     Armed and actually running. The pause during a task's execution (UpgradeLoop) is ignored on
    ///     purpose: it is frequent and would make the indicator flicker.
    /// </summary>
    public static bool IsActive => IsEnabled && _isRunning && _autoUpgradeRoutineHandle != null;
    public static bool IsBalancedCycleActive => _balancedSweepActive;

    public static bool ShouldWaitForTask(bool taskReady, bool taskApproaching) =>
        taskReady || (taskApproaching &&
                      (!UncappedUpgradePolicy.TryParse(_priorityMode?.Value, out var mode) ||
                       mode != UncappedUpgradeMode.Balanced));

    public static IEnumerator WaitForBalancedCycle()
    {
        while (_balancedSweepActive) yield return null;
    }

    private static bool IsShortcutDisabled => _shortcutKey?.Value == KeyCode.None;

    private static KeyCode ShortcutKey =>
        Enum.IsDefined(typeof(KeyCode), _shortcutKey?.Value ?? KeyCode.F6)
            ? _shortcutKey?.Value ?? KeyCode.F6
            : KeyCode.F6;

    public static void Initialize()
    {
        if (_isInitialized) return;

        var clazzName = StringUtils.Humanize(nameof(AutoUpgrade));
        var sectionId = clazzName.Replace(" ", "_").ToLowerInvariant();

        // The display name is the panel's, and it is the plain class name the settings themselves use.
        // The identifier keeps coming from clazzName ('auto_upgrade') because that is the .cfg key.
        var section = MelonPreferences.CreateCategory(sectionId, nameof(AutoUpgrade));
        section.SetFilePath(BotSettings.ConfigPath);

        _shortcutKey = section.CreateEntry(
            "shortcut_key",
            KeyCode.F6,
            "Shortcut Key",
            "The physical key used to manually toggle the AutoUpgrade execution state during gameplay. Default: F6."
        );

        _isEnabled = section.CreateEntry(
            "enabled",
            true,
            "Enable AutoUpgrade",
            "Levels up your heroes, guardian and specials with gold. Starts by itself when the game loads (F6 still toggles it). Default: on."
        );

        _upgradeMode = section.CreateEntry(
            "upgrade_mode",
            "milestone",
            "Upgrade Mode",
            "How many levels each press buys: milestone (up to the next milestone, the best value), max (as many as you can afford), x100, x25, x10 or x1."
        );

        _priorityMode = section.CreateEntry(
            "priority_mode",
            "balanced",
            "Priority Strategy",
            "Balance upgrades each selected slot in order. Focus first spends only on the first visible " +
            "selected slot and saves gold for it when it cannot afford another upgrade."
        );

        _everySeconds = section.CreateEntry(
            "every_seconds",
            60,
            "Upgrade Every (seconds)",
            "Seconds between upgrade bursts. Each burst opens upgrades, buys, and closes the menu so other tasks keep working."
        );

        _upgradeTargetSlots = section.CreateEntry(
            "upgrade_target_slots",
            "",
            "Upgrade Slots",
            "AUTOUPGRADE TARGET SLOT CONFIGURATION. " +
            "\nThis setting controls which upgrade slots (heroes/skills) will be upgraded. " +
            "\nSLOT IDs ARE ZERO-BASED and range from 0 to 6. " +
            "\nORIENTATION: Slot numbering follows the list from top to bottom. " +
            "\nSLOT MAP: 0 = Specials, 1 = Guardian, 2 to 6 = Heroes 1 to 5. " +
            "\nTASK PRIORITY: Main bot tasks have priority over AutoUpgrade. " +
            "\nAUTO PAUSE/RESUME: Balanced uses gaps between tasks and finishes its short pass before a waiting task starts. Focus pauses as soon as a task is approaching. " +
            "\nHOW TO USE: In the panel, toggle the buttons to pick the targets. 'Specials' = slot 0, " +
            "'Guardian' = slot 1 and '1' to '5' = slots 2 to 6. " +
            "\nThe same choice is stored as comma-separated slot IDs, and their order is the priority. " +
            "In the panel, remove and re-add a slot to place it last. " +
            "\nEXAMPLES: '0,6' = only Specials and Hero 5. '3,1,5' = only slots 3, 1 and 5. " +
            "\nTo upgrade every slot, mark all of them ('0,1,2,3,4,5,6'). " +
            "\nIf empty, AutoUpgrade will upgrade all visible slots. " +
            "\nInvalid values are ignored."
        );

        if (BotSettings.EnableAllOnLaunch) _isEnabled.Value = true;
        section.SaveToFile();
        _isInitialized = true;
        Logger.Debug("AutoUpgrade configuration initialized.");
    }

    private static List<CachedGameButton> Buttons()
    {
        var ge = new GameElement(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.UpgradesList);
        ButtonsBuffer.Clear();

        var selectedTargetSlots = GetSelectedTargetSlots();

        if (selectedTargetSlots.Length == 0)
        {
            foreach (var child in ge.GetChildren())
            {
                if (child == null) continue;

                var button = "customUpgrade".Equals(child.Name)
                    ? new CachedGameButton(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.BuyBtn, child)
                    : new CachedGameButton(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.LvlUpBtn, child);

                ButtonsBuffer.Add(button);
            }

            return ButtonsBuffer;
        }

        foreach (var position in selectedTargetSlots)
        {
            var child = ge.GetChild(position);
            if (child == null) continue;

            var button = "customUpgrade".Equals(child.Name)
                ? new CachedGameButton(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.BuyBtn, child)
                : new CachedGameButton(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.LvlUpBtn, child);

            ButtonsBuffer.Add(button);
        }

        return ButtonsBuffer;
    }

    private static int[] GetSelectedTargetSlots()
    {
        if (_upgradeTargetSlots == null) return Array.Empty<int>();

        var value = _upgradeTargetSlots.Value;
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<int>();

        var parts = value.Split(',');
        var result = new List<int>(parts.Length);
        var seen = new bool[7];

        foreach (var part in parts)
        {
            var candidate = part.Trim();
            if (!int.TryParse(candidate, out var slot)) continue;
            if (slot < 0 || slot > 6) continue;
            if (seen[slot]) continue;

            seen[slot] = true;
            result.Add(slot);
        }

        return result.Count > 0 ? result.ToArray() : Array.Empty<int>();
    }

    public static void Update()
    {
        if (!_sceneReady || _shortcutKey == null) return;
        if (!IsShortcutDisabled && !HotkeyGate.IsCapturing && Input.GetKeyDown(ShortcutKey))
        {
            _isEnabled.Value = !IsEnabled;
            MelonPreferences.Save();
        }

        if (IsEnabled) Start();
        else if (_isRunning) Stop();
    }

    public static void OnSceneChanged(bool ready)
    {
        _sceneReady = false;
        Stop(false);
        _sceneReady = ready;
        ButtonsBuffer.Clear();
        if (ready) Start();
    }

    private static void Start()
    {
        if (!_sceneReady || !IsEnabled || _isRunning) return;
        StopCleanup();
        _pausedSince = null;
        _isRunning = true;
        try
        {
            var handle = MelonCoroutines.Start(CoroutineGuard.Run(UpgradeLoop(), "[AutoUpgrade]", AbortAfterFailure));
            if (handle == null) throw new InvalidOperationException("The scheduler returned no worker handle.");
            if (_isRunning) _autoUpgradeRoutineHandle = handle;
            else MelonCoroutines.Stop(handle);
        }
        catch (Exception e)
        {
            Logger.Error($"[AutoUpgrade] Could not start: {e.GetType().Name} - {e.Message}");
            AbortAfterFailure();
        }
        if (_isRunning) Logger.Info("AutoUpgrade started.");
    }

    private static void Stop(bool closeMenu = true)
    {
        StopCleanup();
        var wasRunning = _isRunning;
        _isRunning = false;
        _balancedSweepActive = false;
        _pausedSince = null;
        _activeHold?.CancelHold();
        _activeHold = null;
        if (_autoUpgradeRoutineHandle != null) MelonCoroutines.Stop(_autoUpgradeRoutineHandle);
        _autoUpgradeRoutineHandle = null;
        if (wasRunning && closeMenu && _sceneReady) CloseMenu();
        if (wasRunning) Logger.Info("AutoUpgrade stopped.");
    }

    private static void StopCleanup()
    {
        if (_closeMenuHandle != null) MelonCoroutines.Stop(_closeMenuHandle);
        _closeMenuHandle = null;
    }

    private static void CloseMenu() =>
        _closeMenuHandle = MelonCoroutines.Start(CoroutineGuard.Run(CloseUpgradesMenu(), "[AutoUpgrade] cleanup"));

    private static IEnumerator UpgradeLoop()
    {
        // Burst mode: open, buy, close, wait. The menu is never left open between bursts, so tasks, bubbles and the
        // collector always see the battle screen.
        while (_isRunning)
        {
            if (!BotManager.ShouldPauseAutoUpgrade())
            {
                _pausedSince = null;
                yield return UpgradeCycle();
                if (!_isRunning) yield break;
            }
            else ReportLongPause();

            if (Upgrades.IsVisible) yield return Upgrades.Close;

            var wait = Math.Max(10, _everySeconds?.Value ?? 60);
            var until = Time.unscaledTime + wait;
            while (_isRunning && Time.unscaledTime < until)
            {
                // A task that starts while we wait may have opened the menu itself; leave it alone then.
                yield return IdlePollWait;
            }
        }
    }

    /// <summary>One bounded upgrade burst. Main tasks wait for a balanced burst to finish.</summary>
    private static IEnumerator UpgradeCycle()
    {
        if (!UncappedUpgradePolicy.TryParse(_priorityMode?.Value, out var priorityMode))
        {
            WarnRarely("the priority strategy is invalid; select Balance or Focus first in F1");
            yield break;
        }

        _balancedSweepActive = priorityMode == UncappedUpgradeMode.Balanced;
        try { yield return RunUpgradeCycle(priorityMode); }
        finally
        {
            _activeHold = null;
            _balancedSweepActive = false;
        }
    }

    private static IEnumerator RunUpgradeCycle(UncappedUpgradeMode priorityMode)
    {
        if (!Upgrades.IsVisible)
        {
            yield return new GameButton(Paths.BattleLoc.BottomSideUIDesktopLoc.MenuButtonsLoc.UpgradesBtn).Click();
            var opened = false;
            yield return Firebot.Behaviors.Extra.Ui.WaitUntil(() => Upgrades.IsVisible, v => opened = v, 3f);
            if (!opened)
            {
                WarnRarely("the upgrades menu did not open after clicking its button");
                yield break;
            }

            yield return Upgrades.SetUpgradeLevel(_upgradeMode?.Value ?? "milestone");
            yield return IdlePollWait;
        }

        var buttons = Buttons();
        if (buttons.Count == 0)
        {
            WarnRarely("the upgrades menu has no upgrade buttons for the selected slots");
            yield return null;
            yield break;
        }

        if (EventSystem.current == null)
        {
            WarnRarely("the game's input event system is unavailable");
            yield break;
        }


        var visibleButtons = 0;
        foreach (var buyUpgradeBtn in buttons)
        {
            if (ShouldStopSweep(priorityMode)) break;
            if (!buyUpgradeBtn.IsVisible()) continue;
            visibleButtons++;
            var limit = priorityMode == UncappedUpgradeMode.Focus ? FocusBurstLimit : 1;
            for (var attempt = 0; attempt < limit && !ShouldStopSweep(priorityMode) &&
                 buyUpgradeBtn.IsVisible() && buyUpgradeBtn.IsClickable(); attempt++)
            {
                _activeHold = buyUpgradeBtn;
                yield return buyUpgradeBtn.HoldButton(HoldPerButtonSeconds);
                _activeHold = null;
                yield return GapBetweenButtonsWait;
            }
            if (!UncappedUpgradePolicy.ContinueAfterTarget(priorityMode, true)) break;
        }

        if (visibleButtons == 0 && !BotManager.ShouldPauseAutoUpgrade())
            WarnRarely("no selected upgrade buttons are visible");
        else if (visibleButtons > 0 && _balancedSweepActive &&
                 BotManager.ShouldPauseAutoUpgrade() && !BotManager.IsTaskExecuting)
            Logger.Info($"[AutoUpgrade] Balanced pass finished across {visibleButtons} visible slot(s) before the next task.");
    }

    private static bool ShouldStopSweep(UncappedUpgradeMode priorityMode) =>
        BotManager.IsTaskExecuting ||
        (priorityMode == UncappedUpgradeMode.Focus && BotManager.ShouldPauseAutoUpgrade());

    /// <summary>
    ///     What the guard does when a step throws: the coroutine is already ending, so the state that drives the
    ///     badge and the hotkey is corrected here instead of through <see cref="Stop" />, which would be stopping
    ///     this coroutine from the inside.
    /// </summary>
    private static void AbortAfterFailure()
    {
        _isRunning = false;
        _balancedSweepActive = false;
        _autoUpgradeRoutineHandle = null;
        _activeHold?.CancelHold();
        _activeHold = null;
        _isEnabled.Value = false;
        MelonPreferences.Save();

        // Stop() promises the upgrades menu is not left open, and the loop that would have closed it is gone.
        // Starting a coroutine here is safe, unlike stopping this one from the inside.
        if (_sceneReady) CloseMenu();
        Logger.Warning("[AutoUpgrade] Disabled after the error above. Re-enable in F1 or press F6 to retry.");
    }

    private static IEnumerator CloseUpgradesMenu()
    {
        if (!Upgrades.IsVisible) yield break;
        yield return Upgrades.Close;
    }

    private static DateTime? _pausedSince;
    private static DateTime _lastPauseWarning = DateTime.MinValue;
    private static DateTime _lastCycleWarning = DateTime.MinValue;

    /// <summary>A pause that never ends looks exactly like "auto upgrade does nothing"; say which task holds it.</summary>
    private static void ReportLongPause()
    {
        _pausedSince ??= DateTime.Now;
        if (DateTime.Now - _pausedSince.Value < TimeSpan.FromMinutes(10)) return;
        if (DateTime.Now - _lastPauseWarning < TimeSpan.FromMinutes(10)) return;
        _lastPauseWarning = DateTime.Now;
        Logger.Warning($"[AutoUpgrade] Paused for {(DateTime.Now - _pausedSince.Value).TotalMinutes:0} min: " +
                       $"{BotManager.AutoUpgradePauseReason}.");
    }

    private static void WarnRarely(string what)
    {
        if (DateTime.Now - _lastCycleWarning < TimeSpan.FromMinutes(10)) return;
        _lastCycleWarning = DateTime.Now;
        Logger.Warning($"[AutoUpgrade] Nothing bought: {what}.");
    }
}