using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Firebot.Core;

/// <summary>
///     Reads the numbers the game prints. The game formats them with the player's own system settings, so the same
///     1,528 beer shows as "1,528" on one PC and "1.528" on another (German, Dutch, Spanish and others use the
///     dot for thousands and the comma for decimals). Reading "1.528" as one and a half beers made the Tavern
///     report "1.528 of 1500" and refuse to trade (issue #10).
/// </summary>
public static class GameNumber
{
    private static readonly Regex Token =
        new(@"(\d[\d.,]*)\s*([KMB]?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DotThousands = new(@"^[1-9]\d{0,2}(\.\d{3})+$", RegexOptions.Compiled);
    private static readonly Regex CommaThousands = new(@"^[1-9]\d{0,2}(,\d{3})+$", RegexOptions.Compiled);

    public static double Parse(string text, CultureInfo culture = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        var slash = text.IndexOf('/');
        if (slash >= 0) text = text[..slash];
        var m = Token.Match(text);
        if (!m.Success) return -1;

        var number = m.Groups[1].Value.TrimEnd('.', ',');
        var suffix = m.Groups[2].Value.ToUpperInvariant();
        var percent = text.Length > m.Index + m.Length && text.AsSpan(m.Index + m.Length).TrimStart().StartsWith("%");
        var nf = (culture ?? CultureInfo.CurrentCulture).NumberFormat;
        var dotGroups = nf.NumberGroupSeparator == "." || nf.NumberDecimalSeparator == ",";

        string normal;
        var dot = number.LastIndexOf('.');
        var comma = number.LastIndexOf(',');
        if (dot >= 0 && comma >= 0)
        {
            // Both present: whichever comes last is the decimal point, the other one groups thousands.
            normal = dot > comma ? number.Replace(",", "") : number.Replace(".", "").Replace(',', '.');
        }
        else if (comma >= 0)
        {
            if (CommaThousands.IsMatch(number) || !dotGroups) normal = number.Replace(",", "");
            else normal = number.Replace(',', '.'); // "1,5" with a comma-decimal system
        }
        else if (dot >= 0)
        {
            var groups = DotThousands.IsMatch(number) && suffix.Length == 0 && !percent;
            normal = groups ? number.Replace(".", "") : number;
        }
        else normal = number;

        if (!double.TryParse(normal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            // Something like "1.2.3": keep the leading number, as the game's own labels only ever need that.
            var lead = Regex.Match(normal, @"^\d+(\.\d+)?");
            if (!lead.Success || !double.TryParse(lead.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return -1;
        }

        switch (suffix)
        {
            case "K": value *= 1e3; break;
            case "M": value *= 1e6; break;
            case "B": value *= 1e9; break;
        }

        return value;
    }
}
