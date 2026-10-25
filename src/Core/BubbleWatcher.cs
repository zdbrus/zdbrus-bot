using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Firebot.Core.Tasks;
using UnityEngine;

namespace Firebot.Core;

/// <summary>
///     Watches the notification bubbles on the left of the battle screen. A bubble that no task reacts to is
///     a job the bot is missing: it is written once per session to UserData/screenmaps/unhandled_bubbles.txt.
/// </summary>
public static class BubbleWatcher
{
    private const string Grid = "battleRoot/battleMain/battleCanvas/SafeArea/leftSideUI/notifications/Viewport/grid";
    private static readonly HashSet<string> Reported = new();
    private static HashSet<string> _handled;
    private static float _next;

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 5f;

        try
        {
            _handled ??= HandledBubbles();
            var root = GameObject.Find("battleRoot");
            var grid = root == null ? null : root.transform.Find(Grid["battleRoot/".Length..]);
            if (grid == null) return;

            for (var i = 0; i < grid.childCount; i++)
            {
                var b = grid.GetChild(i);
                if (!b.gameObject.activeInHierarchy || _handled.Contains(b.name) || !Reported.Add(b.name)) continue;
                Logger.Info($"[Bubbles] '{b.name}' is showing and no task handles it yet.");
                var dir = Path.Combine("UserData", "screenmaps");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "unhandled_bubbles.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm}  {b.name}\n");
            }
        }
        catch (Exception e)
        {
            Logger.Debug($"[Bubbles] {e.Message}");
        }
    }

    /// <summary>The bubble names some task reacts to, read from each task's notification path.</summary>
    private static HashSet<string> HandledBubbles()
    {
        var set = new HashSet<string> { "MysteryBox", "CheckIn", "OraclesGift", "ScarabGameShopFreeToken",
            "WarfrontCampaign", "FreePickaxes", "ChaosRiftSupplies", "GuardianHolyUpgrade" };
        var prop = typeof(BotTask).GetProperty("NotificationPath", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var task in BotManager.AllTasks)
        {
            if (prop?.GetValue(task) is string path && path.Length > 0)
                set.Add(path[(path.LastIndexOf('/') + 1)..]);
        }

        return set;
    }
}
