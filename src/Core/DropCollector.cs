using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Firebot.Core;

/// <summary>
///     Clicks the things that wander across the battlefield the moment they appear: the meteorite miners
///     that drop coal, the dragons carrying double beer, and event visitors like the pumpkin head.
///     Runs on its own, whether or not the task bot is started.
/// </summary>
public static class DropCollector
{
    private const string Canvas = "battleRoot/battleMain/battleCanvas";

    // Each entry: the clickable object, and the child that is only active while it is on screen.
    private static readonly (string button, string shownWhen, string label)[] Targets =
    {
        (Canvas + "/MeteoriteHunter", "hunter", "miner"),
        (Canvas + "/CoworkerMeteoriteHunter", "hunter", "miner"),
        (Canvas + "/DragonWithBeer", "dragon", "beer dragon"),
        (Canvas + "/FemaleDragonWithBeer", "femaleDragon", "beer dragon"),
        (Canvas + "/pumpkinHead", null, "pumpkin head")
    };

    private static readonly Dictionary<string, float> LastClick = new();
    private static readonly HashSet<string> Reported = new();
    private static readonly HashSet<string> Clicked = new();
    private static float _nextScan;

    public static void Tick()
    {
        if (!BotSettings.AutoCollectDrops || Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.3f;

        foreach (var (path, shownWhen, label) in Targets)
        {
            try
            {
                var t = Find(path);
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                var shown = t;
                if (shownWhen != null)
                {
                    shown = t.Find(shownWhen);
                    if (shown == null || !shown.gameObject.activeInHierarchy) { Clicked.Remove(path); continue; }
                }

                // Only while it is actually inside the screen, and once per visit.
                if (!OnScreen(shown)) { Clicked.Remove(path); continue; }
                if (Clicked.Contains(path)) continue;
                if (LastClick.TryGetValue(path, out var last) && Time.unscaledTime - last < 2f) continue;
                if (!t.TryGetComponent(out Button b) || !b.interactable)
                {
                    // Not a plain button: try the first button underneath (event visitors vary).
                    b = t.GetComponentInChildren<Button>();
                    if (b == null || !b.interactable) continue;
                }

                b.onClick.Invoke();
                LastClick[path] = Time.unscaledTime;
                Clicked.Add(path);
                if (Reported.Add(label)) Logger.Info($"[Collector] Clicked the {label}.");
                else Logger.Debug($"[Collector] Clicked the {label}.");
            }
            catch (Exception e)
            {
                Logger.Debug($"[Collector] {label}: {e.Message}");
            }
        }
    }

    private static bool OnScreen(Transform t)
    {
        try
        {
            var p = t.position;
            var canvas = t.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                var cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
                if (cam != null) p = cam.WorldToScreenPoint(p);
            }

            return p.x > 0 && p.x < Screen.width && p.y > 0 && p.y < Screen.height;
        }
        catch
        {
            return true; // when unsure, behave as before
        }
    }

    private static Transform Find(string path)
    {
        var slash = path.IndexOf('/');
        var root = GameObject.Find(path[..slash]);
        return root == null ? null : root.transform.Find(path[(slash + 1)..]);
    }
}
