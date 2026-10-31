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
        return (scope ?? "") + "|" + string.Join("/", parts);
    }

    public static bool TryGet(Transform node, string scope, out string name)
    {
        Load();
        return Names.TryGetValue(KeyOf(node, scope), out name) && !string.IsNullOrWhiteSpace(name);
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
