using System;
using System.Collections;
using Firebot.GameModel.Base;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;

namespace Firebot.GameModel.Features.MainScene;

public static class Upgrades
{
    private static string _cachedLevel = string.Empty;

    public static bool IsVisible =>
        new GameElement(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.Root).IsVisible();

    public static IEnumerator Close =>
        new GameButton(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.CloseBtn).Click();

    private static string LvlUpNumTxt =>
        new GameText(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.ChangeLevelUpModeBtnTxt).GetParsedText();

    private static IEnumerator ChangeLevelUpModeBtn =>
        new GameButton(Paths.MenusLoc.CanvasLoc.MainSceneLoc.UpgradesLoc.ChangeLevelUpModeButton).Click();

    /// <summary>
    ///     Cycles the "Upgrade x1 / x10 / x25 / x100 / milestone" button until it shows the wanted mode.
    ///     <paramref name="mode" /> is "milestone", "x100", "x25", "x10" or "x1". If the wanted mode never shows up,
    ///     it settles on x100.
    /// </summary>
    public static IEnumerator SetUpgradeLevel(string mode = "milestone")
    {
        if (!IsVisible) yield break;
        var want = (mode ?? "milestone").Trim().ToLowerInvariant();

        bool Matches(string text)
        {
            var t = (text ?? string.Empty).ToLowerInvariant();
            if (want == "milestone") return t.Contains("milestone") || t.Contains("next");
            if (want == "max") return t.Contains("max") || t.Contains("all");
            // "x1" must not match "x10"/"x100": compare the number after the x.
            var i = t.LastIndexOf('x');
            return i >= 0 && t[(i)..].Trim() == want;
        }

        var current = LvlUpNumTxt;
        if (Matches(current)) { _cachedLevel = current; yield break; }

        string fallback = null;
        var seen = new System.Collections.Generic.List<string> { current };
        for (var n = 0; n < 8; n++)
        {
            yield return ChangeLevelUpModeBtn;
            current = LvlUpNumTxt;
            seen.Add(current);
            if (n == 7) Firebot.Core.Logger.Info($"[AutoUpgrade] Upgrade modes seen: {string.Join(" > ", seen)}");
            if (Matches(current))
            {
                _cachedLevel = current;
                Firebot.Core.Logger.Info($"[AutoUpgrade] Upgrade mode set: {current}");
                yield break;
            }
            if ((current ?? string.Empty).ToLowerInvariant().Contains("x100")) fallback ??= current;
        }

        // Wanted mode not offered by the game: stop on x100 instead of wherever the cycle ended.
        if (fallback == null) yield break;
        for (var n = 0; n < 8 && !string.Equals(LvlUpNumTxt, fallback, StringComparison.Ordinal); n++)
            yield return ChangeLevelUpModeBtn;
        _cachedLevel = LvlUpNumTxt;
    }
}