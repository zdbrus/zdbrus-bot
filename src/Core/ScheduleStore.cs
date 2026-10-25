using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Firebot.Core.Tasks;

namespace Firebot.Core;

/// <summary>Remembers when each task is next due, so restarting the game does not redo every task at once.</summary>
public static class ScheduleStore
{
    private static string FilePath => Path.Combine("UserData", "ZdbrusBotSchedule.txt");

    public static void Save(IEnumerable<BotTask> tasks)
    {
        try
        {
            var lines = new List<string>();
            foreach (var t in tasks)
                if (t.NextRunTime > DateTime.Now)
                    lines.Add($"{t.GetType().Name}={t.NextRunTime.ToString("o", CultureInfo.InvariantCulture)}");
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Logger.Debug($"[Schedule] Save failed: {e.Message}");
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

            foreach (var t in tasks)
                if (map.TryGetValue(t.GetType().Name, out var when) && when > DateTime.Now)
                    t.RestoreNextRun(when);
        }
        catch (Exception e)
        {
            Logger.Debug($"[Schedule] Restore failed: {e.Message}");
        }
    }
}
