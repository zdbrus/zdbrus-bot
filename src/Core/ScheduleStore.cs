using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Firebot.Core.Tasks;

namespace Firebot.Core;

/// <summary>Remembers task schedules and notification retry delays across game restarts.</summary>
public static class ScheduleStore
{
    private static string FilePath => Path.Combine("UserData", "ZdbrusBotSchedule.txt");

    public static void Save(IEnumerable<BotTask> tasks)
    {
        try
        {
            var lines = new List<string>();
            var now = DateTime.Now;
            foreach (var t in tasks)
            {
                var name = t.GetType().Name;
                if (t.NextRunTime != DateTime.MinValue && t.DueTime > now)
                    lines.Add($"{name}={t.NextRunTime.ToString("o", CultureInfo.InvariantCulture)}");
                if (t.BubbleQuietUntil > now)
                    lines.Add($"{name}.BubbleQuietUntil={t.BubbleQuietUntil.ToString("o", CultureInfo.InvariantCulture)}");
                foreach (var deadline in t.RetryDeadlines)
                    if (deadline.Value != DateTime.MinValue)
                        lines.Add($"{name}.{deadline.Key}={deadline.Value.ToString("o", CultureInfo.InvariantCulture)}");
            }
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Logger.Warning($"[Schedule] Save failed: {e.Message}");
        }
    }

    public static void Restore(IEnumerable<BotTask> tasks)
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var map = new Dictionary<string, DateTime>();
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                if (DateTime.TryParse(line[(i + 1)..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                    map[line[..i]] = when;
            }

            var now = DateTime.Now;
            foreach (var t in tasks)
            {
                var name = t.GetType().Name;
                if (map.TryGetValue(name, out var when) && t.DueAt(when) > now)
                    t.RestoreNextRun(when);
                var prefix = name + ".";
                if (map.TryGetValue(prefix + "BubbleQuietUntil", out var quietUntil) && quietUntil > now)
                    t.BubbleQuietUntil = quietUntil;
                foreach (var item in map)
                    if (item.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                        item.Key != prefix + "BubbleQuietUntil")
                        t.RestoreRetryDeadline(item.Key[prefix.Length..], item.Value);
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Logger.Warning($"[Schedule] Restore failed: {e.Message}");
        }
    }
}
