using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Il2CppTMPro;
using UnityEngine;
using Logger = Firebot.Core.Logger;
using UnityEngine.UI;
using Firebot.Core;

namespace Firebot.Behaviors.Extra;

/// <summary>Paths found with the screen map (F9 / UserData/screenmaps).</summary>
internal static class P
{
    public const string Canvas = "menusRoot/menuCanvasParent/SafeArea/menuCanvas";
    public const string Menus = Canvas + "/menus";
    public const string Popups = Canvas + "/popups";
    public const string Events = Canvas + "/events";
    public const string Battle = "battleRoot/battleMain/battleCanvas/SafeArea";

    public const string TownButton = Battle + "/rightSideUI/menuButtons/townButton";
    public const string MapButton = Battle + "/rightSideUI/menuButtons/mapButton";
    public const string GuildButton = Battle + "/rightSideUI/menuButtons/guildButton";
    public const string MailButton = Battle + "/bottomLeftSideUI/mail";
    public const string EventsButton = Battle + "/bottomSideUIDesktop/eventsButton";
    public const string Notifications = Battle + "/leftSideUI/notifications/Viewport/grid";

    public const string Town = Menus + "/TownIrongard";
    public const string TownBuilding = Town + "/townBg/parent/";
    public const string Guild = Menus + "/TownGuild";
    public const string GuildBuilding = Guild + "/";
    public const string RewardsClose = Popups + "/Rewards/bg/closeButton";
}

/// <summary>Small helpers that work on live game objects by path. Every click checks the button is interactable.</summary>
internal static class Ui
{
    private static readonly Regex Tags = new("<[^>]+>");

    public static Transform Find(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var slash = path.IndexOf('/');
        var root = GameObject.Find(slash < 0 ? path : path[..slash]);
        if (root == null) return null;
        return slash < 0 ? root.transform : root.transform.Find(path[(slash + 1)..]);
    }

    public static bool Visible(string path)
    {
        var t = Find(path);
        return t != null && t.gameObject.activeInHierarchy;
    }

    public static bool Clickable(string path) => Clickable(Find(path));

    public static bool Clickable(Transform t)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return false;
        return t.TryGetComponent(out Button b) && b.enabled && b.interactable;
    }

    /// <summary>Clicks if the button is visible and interactable. Returns through <paramref name="done" /> whether it clicked.</summary>
    public static IEnumerator Click(string path, float wait = -1) => Click(Find(path), wait, path);

    public static IEnumerator Click(string path, float wait, string label) => Click(Find(path), wait, label);

    /// <summary>Sends a real pointer down/up/click to the object, for tiles whose logic listens to pointer events instead of Button.onClick.</summary>
    public static void PointerClick(Transform t)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (t == null || es == null) return;
        try
        {
            var data = new UnityEngine.EventSystems.PointerEventData(es)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, clickCount = 1 };
            var go = t.gameObject;
            UnityEngine.EventSystems.ExecuteEvents.Execute(go, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(go, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(go, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
        }
        catch (Exception e)
        {
            Logger.Debug("[Extra] Pointer click failed on " + t.name + ": " + e.Message);
        }
    }

    public static IEnumerator Click(Transform t, float wait = -1, string label = null)
    {
        if (Clickable(t))
        {
            try
            {
                t.GetComponent<Button>().onClick.Invoke();
                if (label != null && !label.Contains('/')) Logger.Info($"[Extra] {label}");
            }
            catch (Exception e)
            {
                Logger.Debug($"[Extra] Click failed on {label ?? t.name}: {e.Message}");
            }
        }
        else
        {
            Logger.Debug($"[Extra] Not clickable: {label ?? t?.name ?? "(missing)"}");
        }

        yield return new WaitForSeconds(wait >= 0 ? wait : BotSettings.InteractionDelay);

        // Safety net: if the game says a currency is missing, close that popup straight away (it only
        // offers "Get more", which leads to the store) and remember it so the caller stops buying.
        MissingCurrency = Visible(CurrencyMissing);
        if (MissingCurrency)
        {
            Logger.Warning($"[Extra] Not enough currency for {label ?? t?.name}; skipped.");
            var close = Find(CurrencyMissing + "/bg/closeButton");
            if (Clickable(close)) close.GetComponent<Button>().onClick.Invoke();
            yield return new WaitForSeconds(0.5f);
        }

        // Locked guardians and machines open an info popup that offers nothing; close it at once so it cannot
        // block the clicks that follow.
        foreach (var locked in LockedPopups)
        {
            if (!Visible(locked)) continue;
            Logger.Debug($"[Extra] Closing {locked.Substring(locked.LastIndexOf('/') + 1)} popup after {label ?? t?.name}.");
            var lockedClose = Find(locked + "/bg/closeButton");
            if (Clickable(lockedClose)) lockedClose.GetComponent<Button>().onClick.Invoke();
            yield return new WaitForSeconds(0.5f);
        }
    }

    private static readonly string[] LockedPopups =
        { P.Popups + "/LockedGuardian", P.Popups + "/LockedMech" };

    public const string CurrencyMissing = P.Popups + "/CurrencyMissing";

    /// <summary>True when the last click was refused for lack of currency.</summary>
    public static bool MissingCurrency { get; private set; }

    /// <summary>Waits for a battle to end and closes its result popup. Returns through the callback whether it was won.</summary>
    public static IEnumerator FinishBattle(Action<bool?> result, float timeout = 90f)
    {
        var end = Time.unscaledTime + timeout;
        while (Time.unscaledTime < end)
        {
            foreach (var popup in VisibleChildren(P.Popups).ToList())
            {
                var n = popup.name;
                if (n.IndexOf("Won", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Lost", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Defeat", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Victory", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("BattleResult", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var won = n.IndexOf("Won", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          n.IndexOf("Victory", StringComparison.OrdinalIgnoreCase) >= 0;
                yield return new WaitForSeconds(0.5f);
                yield return Click(popup.Find("bg/closeButton"), 1.5f, n);
                result(won);
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        result(null);
    }

    public static string Text(string path) => Text(Find(path));

    public static string Text(Transform t)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return string.Empty;
        if (!t.TryGetComponent(out TMP_Text tmp)) return string.Empty;
        try
        {
            return Tags.Replace(tmp.text ?? string.Empty, string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    ///     Reads a plain count such as "2,665", "x5", "0/5" (the part before the slash) or "Level 8/40" (8).
    ///     Returns -1 when the text has no number. Abbreviated values ("1.9K") are expanded.
    /// </summary>
    public static double NumberAt(string path) => Parse(Text(path));

    public static double NumberOf(Transform t) => Parse(Text(t));

    public static double Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        var slash = text.IndexOf('/');
        if (slash >= 0) text = text[..slash];
        var m = Regex.Match(text.Replace(",", ""), @"(\d+(?:\.\d+)?)\s*([KMB]?)", RegexOptions.IgnoreCase);
        if (!m.Success) return -1;
        var value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        switch (m.Groups[2].Value.ToUpperInvariant())
        {
            case "K": value *= 1e3; break;
            case "M": value *= 1e6; break;
            case "B": value *= 1e9; break;
        }

        return value;
    }

    public static IEnumerable<Transform> VisibleChildren(string path)
    {
        var t = Find(path);
        if (t == null) yield break;
        var list = new List<Transform>();
        for (var i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (c.gameObject.activeInHierarchy) list.Add(c);
        }

        foreach (var c in list) yield return c;
    }

    public static IEnumerator WaitFor(string path, float timeout = 5f)
    {
        var end = Time.unscaledTime + timeout;
        while (!Visible(path) && Time.unscaledTime < end) yield return new WaitForSeconds(0.25f);
    }

    public static IEnumerator WaitUntil(Func<bool> condition, Action<bool> completed, float timeout = 2f)
    {
        var end = Time.unscaledTime + timeout;
        while (!condition() && Time.unscaledTime < end) yield return new WaitForSeconds(0.2f);
        completed(condition());
    }

    public static IEnumerator ClickUntil(Transform button, Func<bool> stateChanged, Action<bool> result,
        string label, float clickWait = 1f, float timeout = 2f)
    {
        if (!Clickable(button))
        {
            Logger.Warning($"[Action] {label}: button is not available; no click was attempted.");
            result(false);
            yield break;
        }

        yield return Click(button, clickWait, label);
        var changed = false;
        yield return WaitUntil(stateChanged, value => changed = value, timeout);
        if (!changed) Logger.Warning($"[Action] {label}: clicked, but no confirming game-state change was observed.");
        result(changed);
    }

    /// <summary>Closes the reward popup the game shows after a claim, if it is there.</summary>
    public static IEnumerator CloseRewards()
    {
        if (Visible(P.RewardsClose)) yield return Click(P.RewardsClose);
    }

    /// <summary>
    ///     True when a menu named like the task (for example "Alchemist" for the Alchemist task) is on screen with
    ///     no popup over it, so the task can start right there.
    /// </summary>
    public static bool MenuOpenFor(string taskTitle)
    {
        var key = Norm(taskTitle);
        if (key.Length < 4) return false;
        try
        {
            if (VisibleChildren(P.Popups).Any()) return false;
            return VisibleChildren(P.Menus).Any(m => { var n = Norm(m.name); return n.Length >= 4 && (n == key || n.Contains(key) || key.Contains(n)); });
        }
        catch { return false; }
    }

    private static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static IEnumerator OpenTownBuilding(string building)
    {
        // Already inside this building's screen: nothing to open.
        var target = Norm(building);
        if (VisibleChildren(P.Menus).Any(m => Norm(m.name) == target)) yield break;

        if (!Visible(P.Town))
        {
            yield return Click(P.TownButton, 1.5f);
            yield return WaitFor(P.Town);
        }

        yield return Click(P.TownBuilding + building, 1.5f);
    }

    public static IEnumerator OpenTownSubmenu(string building, string submenu, string button)
    {
        if (Visible(submenu)) yield break;
        yield return OpenTownBuilding(building);
        if (Visible(submenu)) yield break;
        yield return Click(button, 1.5f);
        yield return WaitFor(submenu);
    }

    public static IEnumerator OpenMapSubmenu(string section)
    {
        var map = P.Menus + "/WorldMap";
        var submenu = map + "/submenus/" + section + "Submenu";
        if (!Visible(map))
        {
            yield return Click(P.MapButton, 1.5f);
            yield return WaitFor(map);
        }

        if (!Visible(map) || Visible(submenu)) yield break;
        yield return Click(map + "/submenuButtons/" + section + "Button", 1.5f);
        yield return WaitFor(submenu);
    }

    public static IEnumerator OpenGuildBuilding(string building)
    {
        if (!Visible(P.Guild))
        {
            yield return Click(P.GuildButton, 1.5f);
            yield return WaitFor(P.Guild);
        }

        yield return Click(P.GuildBuilding + building, 1.5f);
    }
}
