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
    private static readonly Dictionary<string, float> SeenSince = new();
    private static readonly Dictionary<string, int> Attempts = new();
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
                    if (shown == null || !shown.gameObject.activeInHierarchy) { Clicked.Remove(path); SeenSince.Remove(path); Attempts.Remove(path); continue; }
                }

                // Only while it is actually inside the screen, and once per visit.
                if (!OnScreen(shown)) { Clicked.Remove(path); SeenSince.Remove(path); Attempts.Remove(path); continue; }
                if (Clicked.Contains(path))
                {
                    // Still on screen well after the click: it likely did not register, so retry a few times.
                    var attempts = Attempts.TryGetValue(path, out var a) ? a : 0;
                    if (attempts < 3 && LastClick.TryGetValue(path, out var clickedAt) &&
                        Time.unscaledTime - clickedAt > 3f)
                    {
                        Logger.Debug($"[Collector] {label} still on screen after the click; retrying.");
                        Clicked.Remove(path);
                    }
                    else continue;
                }
                // Let it settle on screen for a moment first: clicking one mid-spawn may upset the game.
                if (!SeenSince.TryGetValue(path, out var since)) { SeenSince[path] = Time.unscaledTime; continue; }
                if (Time.unscaledTime - since < 1.5f) continue;
                if (LastClick.TryGetValue(path, out var last) && Time.unscaledTime - last < 2f) continue;
                if (!t.TryGetComponent(out Button b) || !b.interactable)
                {
                    // Not a plain button: try the first button underneath (event visitors vary).
                    b = t.GetComponentInChildren<Button>();
                    if (b == null || !b.interactable) continue;
                }

                // Logged before the click so a crash right after it can be traced to the object.
                Logger.Debug($"[Collector] Clicking {t.name} ({label}).");
                LastClick[path] = Time.unscaledTime;
                Clicked.Add(path);
                Attempts[path] = (Attempts.TryGetValue(path, out var n) ? n : 0) + 1;
                b.onClick.Invoke();
                if (Reported.Add(label)) Logger.Info($"[Collector] Clicked the {label}.");
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
