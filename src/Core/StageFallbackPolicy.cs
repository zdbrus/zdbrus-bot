using System;

namespace Firebot.Core;

/// <summary>Why the Boss Retry task steps back from the stage it is stuck on (issue #6).</summary>
public enum StageFallbackReason
{
    None,
    LostBoss,
    TimeAtStage
}

/// <summary>
///     Decides when Boss Retry falls back a few stages instead of pressing "Boss" again. Pure, so the regression
///     tests can drive it without the game.
/// </summary>
public static class StageFallbackPolicy
{
    public const int StagesMin = 1;
    public const int StagesMax = 20;
    public const int MinutesMax = 240;

    /// <summary>
    ///     A boss is lost when the "Boss" button is back up at the same stage the last press was made at. The time
    ///     limit counts from the moment the stage was first reached; 0 turns it off.
    /// </summary>
    public static StageFallbackReason Decide(bool enabled, int stage, int lastAttemptStage, bool fightButtonUp,
        DateTime stageSince, DateTime now, int maxMinutes)
    {
        if (!enabled || stage <= 1 || !fightButtonUp) return StageFallbackReason.None;
        if (lastAttemptStage == stage) return StageFallbackReason.LostBoss;
        if (maxMinutes > 0 && stageSince != default && stageSince <= now &&
            now - stageSince >= TimeSpan.FromMinutes(Math.Min(maxMinutes, MinutesMax)))
            return StageFallbackReason.TimeAtStage;
        return StageFallbackReason.None;
    }

    /// <summary>The stage to step back to: never below stage 1.</summary>
    public static int Target(int stage, int stages)
        => Math.Max(1, stage - Math.Clamp(stages, StagesMin, StagesMax));
}
