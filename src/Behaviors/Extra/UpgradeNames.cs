using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

/// <summary>Persists discovered names, but never prices or completion state, across game sessions.</summary>
internal static class UpgradeNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal);
    private static bool _loaded;
    private static bool _dirty;
    private static string FilePath => Path.Combine("UserData", "ZdbrusBotUpgradeNames.json");

    public static string KeyOf(Transform node, string scope)
    {
        var parts = new List<string>();
        for (var t = node; t != null && parts.Count < 6; t = t.parent)
            parts.Add(t.name + "#" + t.GetSiblingIndex());
        return StableMenuKey((scope ?? "") + "|" + string.Join("/", parts), MenuName(node));
    }

    private static string MenuName(Transform node)
    {
        var depth = 0;
        for (var t = node; t != null && depth < 6; t = t.parent, depth++)
            if (t.parent != null && t.parent.name == "menus") return t.name;
        return null;
    }

    private static string StableMenuKey(string key, string menu)
    {
        if (menu == null) return key;
        var marker = "/" + menu + "#";
        var start = key.LastIndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return key;
        var number = start + marker.Length;
        var end = key.IndexOf('/', number);
        if (end < 0) end = key.Length;
        if (!int.TryParse(key.Substring(number, end - number), out var index) || index < 0) return key;
        // Opening another menu changes this sibling index, but not the upgrade's identity.
        return key.Remove(number - 1, end - number + 1);
    }

    public static bool TryGet(Transform node, string scope, out string name)
    {
        Load();
        var key = KeyOf(node, scope);
        if (Names.TryGetValue(key, out name) && !string.IsNullOrWhiteSpace(name)) return true;
        name = null;
        var menu = MenuName(node);
        if (menu == null) return false;

        foreach (var saved in Names)
        {
            if (StableMenuKey(saved.Key, menu) != key || string.IsNullOrWhiteSpace(saved.Value)) continue;
            if (name != null && !string.Equals(name, saved.Value, StringComparison.OrdinalIgnoreCase))
            {
                name = null;
                Logger.Warning("[Upgrades] Conflicting remembered names; the preview must be read again.");
                return false;
            }
            name = saved.Value;
        }
        if (name == null) return false;
        Names[key] = name;
        _dirty = true;
        return true;
    }

    public static bool Matches(Transform node, string scope, string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        if (string.Equals(expected?.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        Remember(node, scope, actual);
        return false;
    }

    public static void Remember(Transform node, string scope, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Load();
        var key = KeyOf(node, scope);
        name = name.Trim();
        if (Names.TryGetValue(key, out var previous) && previous == name) return;
        Names[key] = name;
        _dirty = true;
    }

    public static void Flush()
    {
        if (!_dirty) return;
        try
        {
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Names));
            File.Move(temp, FilePath, true);
            _dirty = false;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Logger.Warning($"[Upgrades] Could not save remembered upgrade names: {e.Message}");
        }
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(FilePath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            if (saved == null) return;
            foreach (var item in saved)
                if (!string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                    Names[item.Key] = item.Value;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
        {
            Logger.Warning($"[Upgrades] Could not load remembered upgrade names: {e.Message}");
        }
    }
}
