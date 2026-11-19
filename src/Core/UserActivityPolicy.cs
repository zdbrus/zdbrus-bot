namespace Firebot.Core;

/// <summary>When a due task should wait because the player is using a screen of their own.</summary>
public static class UserActivityPolicy
{
    /// <summary>Seconds after the last click, key or scroll during which the player is treated as busy.</summary>
    public const float QuietSeconds = 12f;

    /// <summary>
    ///     True when a task should wait: the player touched the game a moment ago and a screen is open, so that
    ///     screen is probably theirs. A task the player asked for with RUN never waits.
    /// </summary>
    public static bool ShouldWait(float secondsSinceInput, bool screenOpen, bool runRequested)
        => !runRequested && screenOpen && secondsSinceInput < QuietSeconds;
}
