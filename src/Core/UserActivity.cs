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
                Input.mouseScrollDelta.sqrMagnitude > 0f || Input.touchCount > 0 || Input.anyKey || moved)
                _lastInputAt = Time.unscaledTime;
        }
        catch
        {
            // Input can be unavailable for a frame while the game loads: treat it as no activity.
        }
    }

    public static float SecondsSinceInput => Time.unscaledTime - _lastInputAt;
}
