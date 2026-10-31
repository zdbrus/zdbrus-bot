using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Il2Cpp;
using MelonLoader.Utils;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

internal static class ArenaMatchupRecorder
{
    internal const string FileName = "ZdbrusBotArenaMatchups.jsonl";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly ConcurrentQueue<string> WriteErrors = new();
    private static readonly object WriteLock = new();
    private static Task _pending = Task.CompletedTask;

    internal static ArenaMatchupRecord Capture(string preview, DateTime startedAtUtc, double playerPower,
        double opponentPower, int powerLimitPercent, bool dailyException)
    {
        return new ArenaMatchupRecord
        {
            StartedAtUtc = startedAtUtc,
            PlayerPower = playerPower,
            OpponentPower = opponentPower,
            PowerLimitPercent = powerLimitPercent,
            DailyException = dailyException,
            Player = CaptureFormation(preview + "/bg/mask/warMachines/player", "player"),
            Opponent = CaptureFormation(preview + "/bg/mask/warMachines/opponent", "opponent")
        };
    }

    private static ArenaMatchupFormation CaptureFormation(string path, string side)
    {
        var result = new ArenaMatchupFormation();
        var partial = false;
        try
        {
            var root = Ui.Find(path);
            if (root == null || !root.gameObject.activeInHierarchy)
            {
                Logger.Warning($"[Arena] The {side} formation was unavailable for matchup recording.");
                return result;
            }

            var seen = new HashSet<int>();
            foreach (var child in Ui.VisibleChildren(path))
            {
                if (!TryScreenSlot(child.name, out var index))
                {
                    if (child.name.StartsWith("spot", StringComparison.Ordinal))
                    {
                        result.UnreadableSlots++;
                        partial = true;
                    }
                    continue;
                }
                if (index < 0 || index >= 5 || !seen.Add(index))
                {
                    result.UnreadableSlots++;
                    partial = true;
                    continue;
                }

                var slot = new ArenaMatchupSlot { ScreenSlot = index };
                result.Slots.Add(slot);
                try
                {
                    slot.DisplayedLevel = ReadDisplayedLevel(child);
                    slot.DisplayedStars = ReadDisplayedStars(child);
                    if (child.TryGetComponent(out WarMachineFormationSpotFake formationSlot))
                    {
                        var reportedSlot = formationSlot.spotIndex;
                        if (reportedSlot >= 0 && reportedSlot < 5) slot.ReportedSlot = reportedSlot;
                        var machine = formationSlot.warMachine;
                        if (machine != null)
                        {
                            slot.MachineCode = machine.machineCode.ToString();
                            slot.Specialization = machine.specialization.ToString();
                            slot.Level = machine.level > 0 ? machine.level : null;
                            slot.Rarity = machine.rarity >= 0 ? machine.rarity : null;
                            slot.ArenaDamage = Positive(machine.damageTotalArena);
                            slot.ArenaHealth = Positive(machine.healthTotalArena);
                            slot.ArenaArmor = Positive(machine.armorTotalArena);
                            slot.ArenaPower = Positive(machine.arenaPower);
                        }
                    }
                }
                catch (Exception e)
                {
                    partial = true;
                    Logger.Warning($"[Arena] Could not read a {side} preview slot: {e.Message}");
                }

                if (slot.ReportedSlot != slot.ScreenSlot || string.IsNullOrWhiteSpace(slot.MachineCode) ||
                    slot.Level == null || slot.Rarity == null || slot.DisplayedLevel == null ||
                    slot.Level != slot.DisplayedLevel || slot.DisplayedStars == null ||
                    slot.ArenaDamage == null || slot.ArenaHealth == null ||
                    slot.ArenaArmor == null || slot.ArenaPower == null)
                    partial = true;
            }
        }
        catch (Exception e)
        {
            partial = true;
            Logger.Warning($"[Arena] Could not read the {side} formation for matchup recording: {e.Message}");
        }

        result.Status = result.Slots.Count == 0 ? "unavailable" : partial ? "partial" : "observed";
        if (result.Status != "observed")
            Logger.Warning($"[Arena] The {side} matchup snapshot is {result.Status}; missing values are recorded as null.");
        return result;
    }

    private static bool TryScreenSlot(string name, out int index)
    {
        index = 0;
        return name != null && name.StartsWith("spot (", StringComparison.Ordinal) &&
               name.EndsWith(")", StringComparison.Ordinal) &&
               int.TryParse(name.Substring(6, name.Length - 7), NumberStyles.None,
                   CultureInfo.InvariantCulture, out index);
    }

    private static int? ReadDisplayedLevel(Transform slot)
    {
        var text = Ui.Text(slot.Find("warMachineInfo/lvlText"));
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level > 0
            ? level
            : null;
    }

    private static int? ReadDisplayedStars(Transform slot)
    {
        var stars = slot.Find("warMachineInfo/starsParent");
        if (stars == null || !stars.gameObject.activeInHierarchy) return null;
        var found = 0;
        var visible = 0;
        for (var i = 0; i < stars.childCount; i++)
        {
            var star = stars.GetChild(i);
            if (!star.name.StartsWith("star", StringComparison.Ordinal)) continue;
            found++;
            if (star.gameObject.activeInHierarchy) visible++;
        }
        return found == 0 ? null : visible;
    }

    private static double? Positive(double value) => double.IsFinite(value) && value > 0 ? value : null;

    internal static void Enqueue(ArenaMatchupRecord record) =>
        Enqueue(record, Path.Combine(MelonEnvironment.MelonBaseDirectory, "UserData", FileName));

    internal static void Enqueue(ArenaMatchupRecord record, string path)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A recording path is required.", nameof(path));
        var line = JsonSerializer.Serialize(record, JsonOptions) + "\n";

        // Only serialized managed data reaches this worker; Unity and Il2Cpp reads stay on the main thread.
        lock (WriteLock)
            _pending = _pending.ContinueWith(_ => Append(path, line), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static void Append(string path, string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            if (stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                var last = stream.ReadByte();
                stream.Seek(0, SeekOrigin.End);
                if (last != '\n') stream.WriteByte((byte)'\n');
            }
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(line);
        }
        catch (Exception e)
        {
            WriteErrors.Enqueue($"[Arena] Could not save a matchup record: {e.Message}");
        }
    }

    internal static void ReportErrors()
    {
        while (WriteErrors.TryDequeue(out var error)) Logger.Warning(error);
    }

    internal static void Flush()
    {
        Task pending;
        lock (WriteLock) pending = _pending;
        try
        {
            if (!pending.Wait(TimeSpan.FromSeconds(2)))
                Logger.Warning("[Arena] Matchup records were still being saved when the game closed.");
        }
        catch (AggregateException e)
        {
            Logger.Warning($"[Arena] Could not finish saving matchup records: {e.GetBaseException().Message}");
        }
        ReportErrors();
    }
}

internal sealed class ArenaMatchupRecord
{
    public int SchemaVersion { get; set; } = 1;
    public string BotVersion { get; set; } = Firebot.Core.Brand.Version;
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public string Result { get; set; }
    public double PlayerPower { get; set; }
    public double OpponentPower { get; set; }
    public int PowerLimitPercent { get; set; }
    public bool DailyException { get; set; }
    public ArenaMatchupFormation Player { get; set; }
    public ArenaMatchupFormation Opponent { get; set; }
}

internal sealed class ArenaMatchupFormation
{
    public string Status { get; set; } = "unavailable";
    public int UnreadableSlots { get; set; }
    public List<ArenaMatchupSlot> Slots { get; set; } = new();
}

internal sealed class ArenaMatchupSlot
{
    public int ScreenSlot { get; set; }
    public int? ReportedSlot { get; set; }
    public string MachineCode { get; set; }
    public string Specialization { get; set; }
    public int? Level { get; set; }
    public int? Rarity { get; set; }
    public int? DisplayedLevel { get; set; }
    public int? DisplayedStars { get; set; }
    public double? ArenaDamage { get; set; }
    public double? ArenaHealth { get; set; }
    public double? ArenaArmor { get; set; }
    public double? ArenaPower { get; set; }
}
