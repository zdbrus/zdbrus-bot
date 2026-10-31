using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Firebot.Core;

internal static class PurchaseObservation
{
    private static readonly Regex FirstNumber = new(@"\d+", RegexOptions.CultureInvariant);

    public static bool LevelIncreased(string before, string after)
    {
        var oldLevel = FirstNumber.Match(before ?? string.Empty);
        var newLevel = FirstNumber.Match(after ?? string.Empty);
        return oldLevel.Success && newLevel.Success &&
               long.TryParse(oldLevel.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var previous) &&
               long.TryParse(newLevel.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var current) &&
               current > previous;
    }

    public static bool ChangedPrice(double before, double after) =>
        double.IsFinite(before) && double.IsFinite(after) && before > 0 && after > 0 && before != after;

    public static bool Decreased(double before, double after) =>
        double.IsFinite(before) && double.IsFinite(after) && before >= 0 && after >= 0 && after < before;

    public static bool Increased(double before, double after) =>
        double.IsFinite(before) && double.IsFinite(after) && before >= 0 && after >= 0 && after > before;
}
