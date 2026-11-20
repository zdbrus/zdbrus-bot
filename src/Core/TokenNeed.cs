using System;

namespace Firebot.Core;

/// <summary>
///     Expedition tokens a war machine level is waiting for (Aurora's guide: war machines come before the
///     personal tree). Set when a picked machine has its parts but not enough tokens, cleared at the start of
///     each war machine run, and forgotten after a few hours so a disabled war machine task cannot hold
///     tokens forever.
/// </summary>
internal static class TokenNeed
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);
    private static double _amount;
    private static DateTime _at = DateTime.MinValue;

    public static Func<DateTime> Now = () => DateTime.Now;

    public static double Current => _amount > 0 && Now() - _at < Lifetime ? _amount : 0;

    /// <summary>Keeps the largest request of the current war machine run.</summary>
    public static void Request(double tokens)
    {
        if (tokens <= 0 || double.IsNaN(tokens) || double.IsInfinity(tokens)) return;
        if (Current < tokens) _amount = tokens;
        _at = Now();
    }

    /// <summary>
    ///     True when paying cost still leaves what a war machine is waiting for. The guide ranks war machines
    ///     above the Chaos Rift for expedition tokens, so the rift's stone purchases respect this.
    /// </summary>
    public static bool AllowsSpend(double have, double cost) =>
        double.IsFinite(have) && double.IsFinite(cost) && have - cost >= Current;

    public static void Clear()
    {
        _amount = 0;
        _at = DateTime.MinValue;
    }
}
