using System;
using System.Linq;
using UnityEngine;

namespace Firebot.Core;

/// <summary>
///     Notices when the player is using the game, so the scheduler does not close a screen they opened
///     themselves (the Tree of Life, a hero, the shop) just because a task came due.
/// </summary>
public static class UserActivity
{
    private static float _lastInputAt = -1000f;
    private static Vector3 _lastPointer;

    /// <summary>Called once per frame from the mod's update.</summary>
    public static void Tick()
    {
        try
        {
            var pointer = Input.mousePosition;
            var moved = (pointer - _lastPointer).sqrMagnitude > 4f;
            _lastPointer = pointer;
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) ||
                Input.mouseScrollDelta.sqrMagnitude > 0f || Input.touchCount > 0 || PlayerKeyDown() || moved)
                _lastInputAt = Time.unscaledTime;
        }
        catch
        {
            // Input can be unavailable for a frame while the game loads: treat it as no activity.
        }
    }

    private static KeyCode[] _keyboard;

    /// <summary>
    ///     A key the player is holding. The bot's own hotkeys (F1 to F15 and the three configurable ones) do not
    ///     count: pressing F7 or F2 to run the bot is not the player using the game, and it held every task for
    ///     the Wait For Player time (issue #10).
    /// </summary>
    private static bool PlayerKeyDown()
    {
        if (!Input.anyKey) return false;
        _keyboard ??= Keyboard();
        if (_keyboard.Length == 0) return true; // key list unavailable: treat any key as the player
        foreach (var key in _keyboard)
            if (Input.GetKey(key) && !IsBotKey(key)) return true;
        return false;
    }

    private static KeyCode[] Keyboard()
    {
        try
        {
            // Keyboard keys end before the mouse buttons (Mouse0); mouse input is read separately above.
            return Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>()
                .Where(k => k != KeyCode.None && (int)k < (int)KeyCode.Mouse0).ToArray();
        }
        catch
        {
            return Array.Empty<KeyCode>();
        }
    }

    private static bool IsBotKey(KeyCode key)
        => (key >= KeyCode.F1 && key <= KeyCode.F15) ||
           key == BotSettings.ShortcutKey || key == BotSettings.PanelKey || key == BotSettings.StatusKey;

    public static float SecondsSinceInput => Time.unscaledTime - _lastInputAt;
}
