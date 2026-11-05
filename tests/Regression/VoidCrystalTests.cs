using Firebot.Core;
using Firebot.Planner;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using System.Text.Json;

internal static class VoidCrystalTests
{
    private static VoidCrystalOption Gear(int hero, int slot, double cost, int level = 0) =>
        new() { Hero = hero, Name = "Hero " + hero, Slot = slot, Cost = cost, Level = level };
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Ranking()
    {
        var items = new[]
        {
            Gear(1, 0, 30), Gear(2, 0, 15), Gear(1, 3, 120), Gear(8, 3, 60),
            Gear(9, 7, 60), Gear(3, 6, 240), Gear(1, 4, 60), Gear(1, 5, 60),
        };
        var global = VoidCrystalPolicy.Rank(items, "damage", -1).ToArray();
        Check(global.Length == 3 && global[0].Hero == 9 && global[1].Hero == 8,
            "Global damage must include bench relics/wrists, prefer the cheaper upgrade and break ties with relics.");
        var hero = VoidCrystalPolicy.Rank(items, "damage", 1).ToArray();
        Check(hero.Length == 4 && hero[0].Slot == 0 && hero[0].Hero == 1,
            "One-hero mode must include that hero's weapon, not another hero's weapon.");
        Check(VoidCrystalPolicy.Rank(items, "gold", -1).Single().Slot == 6, "Gold must only rank rings.");
        Check(VoidCrystalPolicy.Rank(items, "health", -1).Count() == 2, "Health must rank shoulder and relic.");
        Check(VoidCrystalPolicy.Rank(items, "armor", -1).Count() == 2, "Armor must rank belt and relic.");
        Check(!VoidCrystalPolicy.Rank(items, "power", -1).Any(), "War machine power is not a valid crystal goal.");
        Check(!VoidCrystalPolicy.Rank(items, "damage", -2).Any(), "Invalid targets must not match.");
        var invalid = new[] { Gear(1, 3, double.NaN), Gear(1, 3, double.PositiveInfinity), Gear(1, 3, -1),
            Gear(1, 3, 0), Gear(1, 3, 1.5), Gear(1, 3, 1e20), Gear(-1, 3, 60), Gear(1, 99, 60), Gear(1, 3, 60, -1) };
        Check(!VoidCrystalPolicy.Rank(invalid, "damage", -1).Any(), "Unreadable or invalid gear must never be ranked.");
    }

    public static void Budget()
    {
        var old = Gear(1, 3, 60, 1);
        Check(VoidCrystalPolicy.CanSpend(old, Gear(1, 3, 60, 1), 120, 60, 60), "Exact budget/balance boundary rejected.");
        Check(!VoidCrystalPolicy.CanSpend(old, old, 119, 60, 60), "Budget overrun accepted.");
        Check(!VoidCrystalPolicy.CanSpend(old, old, 120, 60, 59), "Insufficient balance accepted.");
        Check(!VoidCrystalPolicy.CanSpend(old, Gear(1, 3, 120, 1), 1000, 0, 1000), "Changed cost accepted.");
        Check(!VoidCrystalPolicy.CanSpend(old, Gear(1, 3, 60, 2), 1000, 0, 1000), "Stale level accepted.");
        Check(!VoidCrystalPolicy.CanSpend(old, Gear(2, 3, 60, 1), 1000, 0, 1000), "Wrong hero accepted.");
        Check(!VoidCrystalPolicy.CanSpend(old, Gear(1, 4, 60, 1), 1000, 0, 1000), "Wrong slot accepted.");
        Check(!VoidCrystalPolicy.CanSpend(old, null, 1000, 0, 1000), "Missing/capped item accepted.");
        foreach (var value in new[] { -1d, double.NaN, double.PositiveInfinity, 1.5, 1e20 })
        {
            Check(!VoidCrystalPolicy.CanSpend(old, old, value, 0, 1000), "Invalid budget accepted.");
            Check(!VoidCrystalPolicy.CanSpend(old, old, 1000, value, 1000), "Invalid spending total accepted.");
            Check(!VoidCrystalPolicy.CanSpend(old, old, 1000, 0, value), "Invalid currency read accepted.");
        }
        var expensive = Gear(1, 3, 120, 2);
        var cheaper = Gear(2, 3, 60);
        var best = VoidCrystalPolicy.Rank(new[] { expensive, cheaper }, "damage", -1).First();
        Check(best.Hero == 2 && VoidCrystalPolicy.CanSpend(best, best, 120, 60, 60),
            "Re-ranking after a purchase must choose the new best affordable item.");
    }

    public static void Receipts()
    {
        Check(VoidCrystalPolicy.Confirmed(1, 2, 100, 40, 60), "Valid receipt rejected.");
        Check(!VoidCrystalPolicy.Confirmed(1, 1, 100, 40, 60), "Currency loss without a level is not a receipt.");
        Check(!VoidCrystalPolicy.Confirmed(1, 2, 100, 100, 60), "A level without the expected debit is not a receipt.");
        Check(!VoidCrystalPolicy.Confirmed(1, 3, 100, 40, 60), "An unexpected multi-level jump must stop spending.");
        Check(!VoidCrystalPolicy.Confirmed(1, 2, 100, 39, 60), "A different debit must stop spending.");
        Check(!VoidCrystalPolicy.Confirmed(1, 2, double.NaN, 40, 60), "Unreadable balance must stop spending.");
        Check(!VoidCrystalPolicy.Confirmed(1, 2, 100, -1, 101), "Negative balance must stop spending.");
        Check(!VoidCrystalPolicy.Confirmed(-1, 0, 100, 40, 60), "Unreadable level must stop spending.");
    }

    private sealed class Game
    {
        public Currency Balance = new() { amount = 1000 };
        public List<GearItem> Clicked = new();
        public Action<GearItem> Purchase;
        public HOHEnchantSubmenuViewController View = new();
    }

    private static Game Setup()
    {
        VoidCrystalSpender.Cancel();
        Firebot.BotActions.AutoUpgrade.OnSceneChanged(false);
        Firebot.BotActions.AutoSkill.OnSceneChanged(false);
        foreach (var handle in MelonCoroutines.Active.Keys.ToArray()) MelonCoroutines.Stop(handle);
        GameObject.Clear();
        GameObject.BeforeFind = null;
        Firebot.Behaviors.Extra.Ui.BeforeFind = null;
        Firebot.Behaviors.Extra.Ui.OpenTownWorks = true;
        BotManager.IsRunning = BotManager.IsTaskExecuting = false;
        SquadApplier.Busy = JewelSpender.Busy = false;
        GameInitialize.Loaded = true;
        GameInitialize.HandlerLoader = new();
        GameInitialize.MenuLoader = new();
        Time.unscaledTime = 0;
        var game = new Game();
        var loader = GameInitialize.HandlerLoader;
        loader.currencyHandler.currencies[CurrencyCode.VoidCrystal] = game.Balance;
        loader.heroDeck.heroes = new[] { new Hero { code = 0 }, new Hero { code = 1 }, new Hero { code = 2, Unlocked = false } };
        foreach (var h in loader.heroDeck.heroes)
        {
            var server = new HeroServer { code = h.code };
            var slot = h.code == 0 ? GearSlot.Wrist : GearSlot.Relic;
            server.gearsDict[slot] = new GearItem { heroId = (HeroCode)h.code, gearSlot = slot };
            server.gearsDict[GearSlot.Weapon] = new GearItem { heroId = (HeroCode)h.code, gearSlot = GearSlot.Weapon, gearTier = GearTier.Tier1, enchantCost = 30 };
            loader.heroServerHandler.heroesDict[(HeroCode)h.code] = server;
        }
        Button Button(string path) => GameObject.CreatePath(path).AddComponent<Button>();
        game.View.CategoryGearButton = Button("menus/HallOfHeroes/gear");
        Button("menus/HallOfHeroes/submenus/submenuButtons/enchanting");
        UnityEngine.Object.TestInstance = game.View;
        var mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        mech.heroInteractions = loader.heroDeck.heroes.Select(h => new HeroInteractionSquare { hero = h }).ToList();
        mech.OnSelect = id =>
        {
            mech.heroServerSelected = loader.heroServerHandler.heroesDict[(HeroCode)id];
            game.View.GearItems = mech.heroServerSelected.gearsDict.Select(pair =>
            {
                var gear = pair.Value;
                var button = Button("menus/HallOfHeroes/items/" + id + "/" + pair.Key);
                button.onClick.Action = () =>
                {
                    game.Clicked.Add(gear);
                    if (game.Purchase != null) game.Purchase(gear);
                    else
                    {
                        game.Balance.amount -= gear.enchantCost.GetDecrypted();
                        gear.EnchantLevel++;
                        gear.enchantCost = gear.enchantCost.GetDecrypted() * 2;
                    }
                };
                return new EnchantGearItemInteraction { slot = pair.Key, gearItem = gear, enchantGearItemButton = button };
            }).ToArray();
        };
        mech.OnSelect(0);
        return game;
    }

    private static JsonElement Snapshot()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            VoidCrystalSpender.WriteSnapshot(writer);
            writer.WriteEndObject();
        }
        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.GetProperty("crystals").Clone();
    }

    private static string Packet(double budget = 120, string goal = "damage", int target = -1) =>
        JsonSerializer.Serialize(new { action = "start", epoch = Snapshot().GetProperty("epoch").GetString(),
            requestId = Guid.NewGuid().ToString(), budget, goal, target });

    private static void Pump(Action onYield = null)
    {
        var pair = MelonCoroutines.Active.Single();
        var n = 0;
        while (pair.Value.MoveNext())
        {
            Check(++n < 4000, "Spending coroutine did not terminate.");
            Time.unscaledTime += 0.25f;
            onYield?.Invoke();
        }
        MelonCoroutines.Stop(pair.Key);
    }

    public static void Spending()
    {
        var game = Setup();
        var options = VoidCrystalSpender.ReadOptions();
        Check(options.Count == 4 && options.All(o => o.Hero != 2), "Unowned gear leaked into the advisor.");
        var packet = Packet();
        Check(VoidCrystalSpender.Request(packet) == "Spending", "Valid request rejected.");
        Pump();
        Check(!VoidCrystalSpender.Busy && VoidCrystalSpender.Ok && game.Clicked.Count == 2 && game.Balance.amount == 880,
            "Actual spending loop did not enforce the exact budget.");
        Check(game.Clicked[0].heroId == HeroCode.Burt && game.Clicked[1].heroId == HeroCode.Talia,
            "Actual spending did not re-rank after upgrading the benched hero's relic.");
        Check(VoidCrystalSpender.Request(packet) != "Spending", "Completed request replayed.");

        game = Setup();
        Check(VoidCrystalSpender.Request(Packet(30, target: 0)) == "Spending", "Personal gear request rejected.");
        Pump();
        Check(game.Clicked.Count == 1 && game.Clicked[0].gearSlot == GearSlot.Weapon && game.Balance.amount == 970,
            "Personal mode did not buy the selected hero's weapon.");

        game = Setup();
        var capped = GameInitialize.HandlerLoader.heroServerHandler.heroesDict[HeroCode.Burt].gearsDict[GearSlot.Relic];
        capped.EnchantLevel = capped.MaxLevel;
        GameInitialize.HandlerLoader.heroServerHandler.heroesDict[HeroCode.Talia].gearTier = 1;
        Check(!VoidCrystalPolicy.Rank(VoidCrystalSpender.ReadOptions(), "damage", -1).Any(),
            "Maxed gear or a locked tier remained eligible.");

        game = Setup();
        foreach (var server in GameInitialize.HandlerLoader.heroServerHandler.heroesDict.Values)
        {
            server.gearTier = 0;
            foreach (var gear in server.gearsDict.Values) gear.MaxLevel = 0;
        }
        var fresh = VoidCrystalSpender.ReadOptions();
        Check(fresh.Count == 2 && fresh.All(o => o.Slot == (int)GearSlot.Weapon),
            "A new account's tier 1 gear was hidden by a zero tier count or the game's per-rarity flag.");
        Check(VoidCrystalSpender.Skipped.StartsWith("2 upgradable") && VoidCrystalSpender.Skipped.Contains("2 in a locked tier"),
            "The gear scan summary did not explain skipped items.");
    }

    public static void SpendingFailures()
    {
        foreach (var mode in new[] { "debit", "level", "throw" })
        {
            var game = Setup();
            game.Purchase = gear =>
            {
                if (mode == "throw") throw new InvalidOperationException("simulated click failure");
                if (mode == "debit") game.Balance.amount -= gear.enchantCost.GetDecrypted();
                if (mode == "level") gear.EnchantLevel++;
            };
            Check(VoidCrystalSpender.Request(Packet(1000)) == "Spending", "Failure scenario could not start.");
            Pump();
            Check(!VoidCrystalSpender.Busy && !VoidCrystalSpender.Ok && game.Clicked.Count == 1,
                "An unconfirmed or failing purchase was retried or left a stuck busy flag.");
        }
        var changed = Setup();
        Check(VoidCrystalSpender.Request(Packet()) == "Spending", "Changed-price scenario could not start.");
        Firebot.Behaviors.Extra.Ui.BeforeFind = path =>
        {
            if (path.EndsWith("/enchanting"))
                GameInitialize.MenuLoader.HallOfHeroesMech.heroServerSelected.gearsDict[GearSlot.Relic].enchantCost = 61;
        };
        Pump();
        Check(changed.Clicked.Count == 0 && !VoidCrystalSpender.Ok, "A changed live price was spent.");
        Firebot.Behaviors.Extra.Ui.BeforeFind = null;

        var waiting = Setup();
        BotManager.IsRunning = BotManager.IsTaskExecuting = true;
        Check(VoidCrystalSpender.Request(Packet()) == "Spending", "Waiting scenario could not start.");
        Pump();
        Check(waiting.Clicked.Count == 0 && !VoidCrystalSpender.Ok, "Bot wait timeout proceeded to spend.");
        BotManager.IsRunning = BotManager.IsTaskExecuting = false;

        var cancelled = Setup();
        Check(VoidCrystalSpender.Request(Packet()) == "Spending", "Cancel scenario could not start.");
        Pump(() => { if (VoidCrystalSpender.Busy) VoidCrystalSpender.Cancel(); });
        Check(cancelled.Clicked.Count == 0 && !VoidCrystalSpender.Busy, "Cancellation still allowed a purchase.");
    }

    public static void RequestSafety()
    {
        Setup();
        foreach (var bad in new[] { "{}", "{", Packet(0), Packet(-1), Packet(1001), Packet(1.5), Packet(60, "power"), Packet(60, target: -2) })
            Check(VoidCrystalSpender.Request(bad) != "Spending", "Invalid request accepted: " + bad);
        var stale = Packet();
        VoidCrystalSpender.Cancel();
        Check(VoidCrystalSpender.Request(stale) != "Spending", "Old game session accepted.");
        JewelSpender.Busy = true;
        Check(VoidCrystalSpender.Request(Packet()) != "Spending", "Overlapping jewel spending accepted.");
        JewelSpender.Busy = false;
        SquadApplier.Busy = true;
        Check(VoidCrystalSpender.Request(Packet()) != "Spending", "Overlapping squad apply accepted.");
        SquadApplier.Busy = false;
        GameInitialize.HandlerLoader.CharacterLevel.Level = 5;
        GameInitialize.MenuLoader.TutorialGuidance.isCurrentTutorialPartInProgress = true;
        GameInitialize.MenuLoader.TutorialGuidance.isTutorialFinished = false;
        Check(VoidCrystalSpender.Request(Packet()) != "Spending", "Tutorial spending accepted.");
        Setup();
        GameInitialize.HandlerLoader.heroServerHandler.heroesDict[HeroCode.Talia].gearsDict[GearSlot.Wrist].ThrowOnCostText = true;
        var snapshot = Snapshot();
        Check(!snapshot.GetProperty("available").GetBoolean() && snapshot.GetProperty("items").GetArrayLength() == 0,
            "Unreadable gear produced a success-shaped snapshot.");
        Check(VoidCrystalSpender.Request(Packet()) != "Spending", "Unreadable gear allowed spending.");
        Check(!VoidCrystalSpender.Busy, "Rejected request left busy set.");
    }
}
