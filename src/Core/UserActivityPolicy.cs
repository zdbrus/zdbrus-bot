namespace Firebot.Core;

/// <summary>When a due task should wait because the player is using the game themselves.</summary>
public static class UserActivityPolicy
{
    /// <summary>Default seconds after the last click, key, scroll or mouse move during which the player is busy.</summary>
    public const float QuietSeconds = 20f;

    public const float QuietSecondsMin = 5f;
    public const float QuietSecondsMax = 120f;

    /// <summary>
    ///     A screen the player opened stays theirs this many times longer than the plain quiet window: reading a
    ///     hero, the shop or the tree without touching the mouse is normal, and closing it under them is what
    ///     players notice most.
    /// </summary>
    public const float OpenScreenFactor = 3f;

    /// <summary>
    ///     True when a task (or the idle cleanup) should wait. The player touched the game a moment ago, on any
    ///     screen, or a screen the bot did not open is still up and they touched the game within the longer
    ///     open-screen window. A task the player asked for with RUN never waits.
    /// </summary>
    public static bool ShouldWait(float secondsSinceInput, bool screenOpen, bool runRequested,
        float quietSeconds = QuietSeconds)
    {
        if (runRequested) return false;
        if (quietSeconds < QuietSecondsMin) quietSeconds = QuietSecondsMin;
        if (secondsSinceInput < quietSeconds) return true;
        return screenOpen && secondsSinceInput < quietSeconds * OpenScreenFactor;
    }
}
