using System;

namespace Firebot.Core;

internal static class LiberationDifficultyPolicy
{
    public static bool TryGetTargets(string value, out int[] targets)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "easy":
                targets = new[] { 1 };
                return true;
            case "normal":
                targets = new[] { 2 };
                return true;
            case "both":
                targets = new[] { 1, 2 };
                return true;
            default:
                targets = Array.Empty<int>();
                return false;
        }
    }

    public static bool TryGetStep(int currentStars, int targetStars, out int direction)
    {
        direction = 0;
        if (currentStars is < 1 or > 2 || targetStars is < 1 or > 2) return false;
        direction = Math.Sign(targetStars - currentStars);
        return true;
    }
}
