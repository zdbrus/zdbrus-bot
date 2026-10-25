using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Firebot.Core;

/// <summary>
///     Writes a map of what is on screen (every visible object's path, its buttons and its text) to
///     UserData/screenmaps. New features are wired from these maps.
///     F9 writes a full map. With auto mapping on, every menu or popup is mapped the first time it opens.
/// </summary>
public static class ScreenMap
{
    private const string CanvasRoot = "menusRoot/menuCanvasParent/SafeArea/menuCanvas";
    private static readonly string[] WatchedRoots = { CanvasRoot + "/menus", CanvasRoot + "/popups", CanvasRoot + "/events" };
    private static readonly HashSet<string> Mapped = new();
    private static readonly HashSet<string> Boring = new()
    {
        "Transform", "RectTransform", "CanvasRenderer", "Image", "LayoutElement", "ContentSizeFitter",
        "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup", "Mask", "RectMask2D", "Shadow", "Outline"
    };

    private static float _nextCheck;
    public static KeyCode Key => KeyCode.F9;

    private static string Folder
    {
        get
        {
            var dir = Path.Combine("UserData", "screenmaps");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void Tick()
    {
        if (Input.GetKeyDown(Key)) DumpAll();

        if (!BotSettings.AutoScreenMap || Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 1.5f;

        foreach (var rootPath in WatchedRoots)
        {
            var root = Find(rootPath);
            if (root == null) continue;
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                var key = rootPath + "/" + child.name;
                if (!Mapped.Add(key)) continue;
                var kind = rootPath.Substring(rootPath.LastIndexOf('/') + 1);
                Write($"{kind}_{Safe(child.name)}.txt", child, key);
            }
        }
    }

    public static void DumpAll()
    {
        try
        {
            var sb = new StringBuilder();
            for (var s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                foreach (var go in scene.GetRootGameObjects())
                    if (go.activeInHierarchy) Walk(go.transform, go.name, 0, sb);
            }

            var name = $"full_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            File.WriteAllText(Path.Combine(Folder, name), sb.ToString());
            Logger.Info($"[ScreenMap] Saved {name}");
        }
        catch (Exception e)
        {
            Logger.Error($"[ScreenMap] Full map failed: {e.Message}");
        }
    }

    private static void Write(string fileName, Transform root, string path)
    {
        try
        {
            var sb = new StringBuilder();
            Walk(root, path, 0, sb);
            File.WriteAllText(Path.Combine(Folder, fileName), sb.ToString());
            Logger.Info($"[ScreenMap] Saved {fileName}");
        }
        catch (Exception e)
        {
            Logger.Error($"[ScreenMap] Map of {path} failed: {e.Message}");
        }
    }

    private static void Walk(Transform t, string path, int depth, StringBuilder sb)
    {
        if (depth > 40 || !t.gameObject.activeInHierarchy) return;

        sb.Append(path);
        Describe(t, sb);
        sb.Append('\n');

        for (var i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (c.gameObject.activeInHierarchy)
                Walk(c, path + "/" + c.name, depth + 1, sb);
            else
                sb.Append(path).Append('/').Append(c.name).Append("  (hidden)\n");
        }
    }

    private static void Describe(Transform t, StringBuilder sb)
    {
        try
        {
            if (t.TryGetComponent(out Button b))
                sb.Append(b.interactable ? "  [BUTTON]" : "  [BUTTON off]");
            if (t.TryGetComponent(out Toggle tg))
                sb.Append(tg.isOn ? "  [TOGGLE on]" : "  [TOGGLE off]");
            if (t.TryGetComponent(out TMP_Text tmp))
            {
                var text = tmp.text ?? string.Empty;
                text = text.Replace("\n", " ").Replace("\r", " ");
                if (text.Length > 70) text = text.Substring(0, 70) + "...";
                if (text.Length > 0) sb.Append("  \"").Append(text).Append('"');
            }

            var extra = new List<string>();
            foreach (var comp in t.GetComponents<Component>())
            {
                if (comp == null) continue;
                var n = comp.GetIl2CppType().Name;
                if (Boring.Contains(n) || n == "Button" || n == "Toggle" || n.StartsWith("TextMeshPro")) continue;
                extra.Add(n);
            }

            if (extra.Count > 0) sb.Append("  {").Append(string.Join(",", extra)).Append('}');
        }
        catch
        {
            // a component that cannot be read is skipped; the path is what matters
        }
    }

    private static Transform Find(string path)
    {
        var slash = path.IndexOf('/');
        var root = GameObject.Find(slash < 0 ? path : path[..slash]);
        if (root == null) return null;
        return slash < 0 ? root.transform : root.transform.Find(path[(slash + 1)..]);
    }

    private static string Safe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
