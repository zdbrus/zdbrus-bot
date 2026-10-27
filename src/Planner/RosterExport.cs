using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace Firebot.Planner;

/// <summary>
///     Snapshot of everything the War Machine Planner needs, read from the game's own data (never from the screen).
///     <para>
///         Runs on the main thread every few seconds and stores the result as a JSON string. The planner server hands
///         that string out from its own thread, so the game's objects are only ever touched here. Read-only: nothing
///         is written back to the game.
///     </para>
///     <para>
///         Besides the inputs (levels, rarities, jewels, crews) it copies the game's own finished numbers for each
///         machine (damage, health, armor, power with and without crew). The planner shows its math next to those, so
///         any formula that drifts from the game is visible at once.
///     </para>
/// </summary>
public static class RosterExport
{
    private const float Interval = 3f;
    private static float _next;
    private static string _lastError;

    /// <summary>Latest snapshot as JSON, or null before the first good read.</summary>
    public static volatile string Json;

    /// <summary>When the snapshot was taken (UTC), for the page's "live" indicator.</summary>
    public static DateTime TakenUtc;

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + Interval;
        if (!PlannerServer.HasClients) return; // nobody is looking: skip the work

        try
        {
            var json = Build();
            if (json != null) { Json = json; TakenUtc = DateTime.UtcNow; }
        }
        catch (Exception e)
        {
            var msg = $"{e.GetType().Name}: {e.Message}";
            if (msg != _lastError) Core.Logger.Warning($"[Planner] Roster read failed: {msg}");
            _lastError = msg;
        }
    }

    private static string Build()
    {
        if (!GameInitialize.HasGameLoaded()) return null;
        var loader = GameInitialize.HandlerLoader;
        if (loader == null) return null;
        var wmh = loader.warMachineHandler;
        var heroes = loader.heroServerHandler;
        if (wmh == null || heroes == null || !wmh.hasLoaded || !heroes.hasLoaded) return null;

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteString("taken", DateTime.UtcNow.ToString("o"));

            // ---- account ----
            var eng = loader.engineerLevel;
            w.WriteNumber("engineerLevel", eng?.level ?? 0);
            w.WriteNumber("crewSlots", eng?.heroSpots ?? 0);
            Num(w, "engineerEffect", eng?.engineerEffect ?? 0);
            Num(w, "formationBattlePower", wmh.myFormationBattlePower);
            Num(w, "formationArenaPower", wmh.myFormationArenaPower);
            Num(w, "allMachinesRarityBuff", Safe(() => wmh.CalculateAllWarmachinesRarityBuff()));

            // ---- artifacts: the game's own totals for the war machine attributes ----
            w.WriteStartObject("artifacts");
            var arts = loader.ancientArtifactHandler?.allArtifactAttributes;
            if (arts != null)
                foreach (var kv in arts)
                {
                    var key = kv.Key.ToString();
                    if (key.Contains("WarMachine") || key is "MechanicalFury" or "Overdrive")
                        Num(w, key, kv.Value.ToDouble());
                }
            w.WriteEndObject();

            // ---- war machines (owned) ----
            w.WriteStartArray("machines");
            var data = wmh.warMachineData;
            if (data?.warMachineList != null)
                for (var i = 0; i < data.warMachineList.Count; i++)
                {
                    var m = data.warMachineList[i];
                    if (m == null) continue;
                    WriteMachine(w, m);
                }
            w.WriteEndArray();

            // ---- active squad ----
            w.WriteStartArray("squad");
            if (data?.formationList != null)
                for (var i = 0; i < data.formationList.Count; i++)
                {
                    var s = data.formationList[i];
                    if (s == null || string.IsNullOrEmpty(s.code)) continue;
                    w.WriteStartObject();
                    w.WriteNumber("spot", s.spotIndex);
                    w.WriteString("code", s.code);
                    w.WriteStartArray("crew");
                    if (s.warMachineHeroCodes != null)
                        for (var h = 0; h < s.warMachineHeroCodes.Count; h++) w.WriteNumberValue(s.warMachineHeroCodes[h]);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
            w.WriteEndArray();

            // ---- heroes ----
            w.WriteStartArray("heroes");
            var unlocked = heroes.heroesUnlockState;
            foreach (var kv in heroes.heroesDict)
            {
                var hero = kv.Value;
                if (hero == null) continue;
                var owned = unlocked != null && unlocked.ContainsKey(kv.Key) && unlocked[kv.Key];
                if (!owned && heroes.mercenariesHired != null && heroes.mercenariesHired.Contains(kv.Key)) owned = true;
                if (!owned && heroes.godsHired != null && heroes.godsHired.Contains(kv.Key)) owned = true;
                WriteHero(w, hero, owned);
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteMachine(Utf8JsonWriter w, WarMachine m)
    {
        w.WriteStartObject();
        w.WriteString("code", m.machineCode.ToString());
        w.WriteString("id", m.code ?? "");
        w.WriteString("name", m.name ?? m.machineCode.ToString());
        w.WriteString("spec", m.specialization.ToString());
        w.WriteNumber("level", m.level);
        w.WriteNumber("rarity", m.rarity);
        w.WriteNumber("sacredCards", m.sacredCards);
        w.WriteNumber("lostInscriptions", m.lostInscriptions);
        w.WriteNumber("slot", m.slot);
        w.WriteBoolean("favourite", m.isFavourite);

        w.WriteStartObject("blueprints");
        if (m.blueprintUpgradesDict != null)
            foreach (var kv in m.blueprintUpgradesDict)
                if (kv.Value != null) w.WriteNumber(kv.Key.ToString().ToLowerInvariant(), kv.Value.level);
        w.WriteEndObject();

        w.WriteStartArray("crew");
        if (m.crewHeroIds != null)
            for (var i = 0; i < m.crewHeroIds.Count; i++) w.WriteNumberValue(m.crewHeroIds[i]);
        w.WriteEndArray();

        // The game's own numbers, used by the planner as the answer key.
        w.WriteStartObject("game");
        Num(w, "damageInitial", m.damageInitial);
        Num(w, "healthInitial", m.healthInitial);
        Num(w, "armorInitial", m.armorInitial);
        Num(w, "damageNoCrew", m.damageTotalNoCrew);
        Num(w, "healthNoCrew", m.healthTotalNoCrew);
        Num(w, "armorNoCrew", m.armorTotalNoCrew);
        Num(w, "damage", m.damageTotal);
        Num(w, "health", m.healthTotal);
        Num(w, "armor", m.armorTotal);
        Num(w, "damageArena", m.damageTotalArena);
        Num(w, "healthArena", m.healthTotalArena);
        Num(w, "armorArena", m.armorTotalArena);
        Num(w, "power", m.power);
        Num(w, "powerNoCrew", m.powerNoCrew);
        Num(w, "arenaPower", m.arenaPower);
        Num(w, "levelMultiplier", m.levelMultiplier);
        var cm = m.crewMultipliers;
        Num(w, "crewDamage", cm?.damageFinalMultiplier ?? 0);
        Num(w, "crewHealth", cm?.healthFinalMultiplier ?? 0);
        Num(w, "crewArmor", cm?.armorFinalMultiplier ?? 0);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteHero(Utf8JsonWriter w, HeroServer h, bool owned)
    {
        w.WriteStartObject();
        w.WriteNumber("id", h.code);
        w.WriteString("code", h.heroCode.ToString());
        w.WriteString("spec", h.specialization.ToString());
        w.WriteNumber("rarity", (int)h.heroRarity);
        w.WriteBoolean("owned", owned);

        w.WriteStartArray("jewels");
        if (h.jewelDict != null)
            foreach (var kv in h.jewelDict)
            {
                var j = kv.Value;
                if (j == null || (int)j.rarity == 0) continue;
                w.WriteStartObject();
                w.WriteString("slot", kv.Key.ToString());
                w.WriteNumber("tier", (int)j.gearTier);
                w.WriteNumber("rarity", (int)j.rarity);
                w.WriteNumber("level", j.enchantLevel);
                Num(w, "effect", j.effect);
                w.WriteEndObject();
            }
        w.WriteEndArray();

        var je = h.jewelEffects;
        var cb = h.crewBonuses;
        w.WriteStartObject("game");
        Num(w, "jewelDamage", je?.damage ?? 0);
        Num(w, "jewelHealth", je?.health ?? 0);
        Num(w, "jewelArmor", je?.armor ?? 0);
        Num(w, "crewDamage", cb?.damage ?? 0);
        Num(w, "crewHealth", cb?.health ?? 0);
        Num(w, "crewArmor", cb?.armor ?? 0);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    // JSON has no Infinity/NaN: write those as null so the page never fails to parse.
    private static void Num(Utf8JsonWriter w, string name, double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) w.WriteNull(name);
        else w.WriteNumber(name, v);
    }

    private static double Safe(Func<double> f)
    {
        try { return f(); } catch { return double.NaN; }
    }
}
