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
        new()
        {
            Hero = hero, Name = "Hero " + hero, Slot = slot, Cost = cost, Level = level,
            Rarity = 1, NativeEffect = 3, EffectGain = 400, MultiplierGain = 1, PowerGain = 100,
            Active = true, DamageReady = true, HealthReady = true, ArmorReady = true,
            DamageShare = 1, HealthShare = 1, ArmorShare = 1,
        };
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static GearItem LiveGear(HeroCode hero, GearSlot slot) =>
        GameInitialize.HandlerLoader.heroServerHandler.heroesDict[hero].heroGears.Single(g => g.gearSlot == slot);

    public static void Ranking()
    {
        var ring = Gear(1, 6, 239);
        var relic = Gear(2, 7, 60);
        var wrist = Gear(3, 3, 60);
        var balanced = new[] { ring, relic, wrist };
        Check(VoidCrystalPolicy.Rank(balanced, "balanced", -1).First() == ring,
            "Rings must be favored below four times the next gear cost.");
        ring.Cost = 240;
        Check(VoidCrystalPolicy.Rank(balanced, "balanced", -1).First() == relic,
            "At exactly four times the cost, cheaper gear must catch up.");
        ring.Cost = 241;
        Check(VoidCrystalPolicy.Rank(balanced, "balanced", -1).Last() == ring,
            "Rings above the four-times threshold must yield to other gear.");
        Check(VoidCrystalPolicy.Rank(balanced, "balanced", 1).Single() == ring,
            "Balanced hero targeting must not include another hero's global gear.");
        var personal = Gear(9, 0, 30); personal.Active = false; personal.DamageReady = false;
        Check(VoidCrystalPolicy.Rank(balanced.Append(personal), "balanced", -1).First() == personal,
            "Balanced upgrades must include cheaper personal gear without requiring party stats.");
        personal.Cost = double.NaN;
        Check(!VoidCrystalPolicy.Rank(new[] { personal }, "balanced", -1).Any(),
            "Balanced upgrades must reject unreadable costs.");
        var weakWeapon = Gear(1, 0, 10); weakWeapon.DamageShare = 0.05;
        var strongWeapon = Gear(2, 0, 20); strongWeapon.DamageShare = 0.9;
        var benchWeapon = Gear(9, 0, 5); benchWeapon.Active = false; benchWeapon.PowerGain = 900;
        var benchWrist = Gear(8, 3, 60); benchWrist.Active = false;
        var benchRelic = Gear(9, 7, 60, 3); benchRelic.Active = false; benchRelic.MultiplierGain = 15d / 8 - 1;
        var items = new[] { weakWeapon, strongWeapon, benchWeapon, benchWrist, benchRelic,
            Gear(3, 6, 240), Gear(1, 4, 60), Gear(1, 5, 60) };
        var global = VoidCrystalPolicy.Rank(items, "damage", -1).ToArray();
        Check(global.Length == 4 && global[0] == strongWeapon && global.Contains(benchWrist) &&
            global.Contains(benchRelic) && !global.Contains(benchWeapon),
            "Auto must compare party contribution and next-level gain, include bench globals and exclude bench personal gear.");
        Check(Array.IndexOf(global, benchWrist) < Array.IndexOf(global, benchRelic),
            "Level 3 to 4 is not an exact doubling; the stronger next enchant must rank first.");
        var hero = VoidCrystalPolicy.Rank(items, "damage", 1).ToArray();
        Check(hero.Length == 3 && hero[0] == weakWeapon,
            "One-hero mode must include that hero's weapon, not another hero's weapon.");
        Check(VoidCrystalPolicy.Rank(items, "gold", -1).Single().Slot == 6, "Gold must only rank rings.");
        Check(VoidCrystalPolicy.Rank(items, "health", -1).Count() == 2, "Health must rank shoulder and relic.");
        Check(VoidCrystalPolicy.Rank(items, "armor", -1).Count() == 2, "Armor must rank belt and relic.");
        Check(VoidCrystalPolicy.Rank(items, "power", -1).First() == benchWeapon,
            "Gear power must compare every owned item, including personal gear on the bench.");
        Check(VoidCrystalPolicy.Rank(items, "power", 1).All(i => i.Hero == 1),
            "A hero-specific gear power goal must only rank that hero's gear.");
        Check(!VoidCrystalPolicy.Rank(items, "damage", -2).Any(), "Invalid targets must not match.");
        var invalid = new[] { Gear(1, 3, double.NaN), Gear(1, 3, double.PositiveInfinity), Gear(1, 3, -1),
            Gear(1, 3, 0), Gear(1, 3, 1.5), Gear(1, 3, 1e20), Gear(-1, 3, 60), Gear(1, 99, 60), Gear(1, 3, 60, -1) };
        Check(!VoidCrystalPolicy.Rank(invalid, "damage", -1).Any(), "Unreadable or invalid gear must never be ranked.");
        var badGain = Gear(1, 3, 60); badGain.MultiplierGain = double.NaN;
        var noParty = Gear(1, 3, 60); noParty.DamageReady = false;
        Check(!VoidCrystalPolicy.Rank(new[] { badGain, noParty }, "damage", -1).Any(),
            "Unreadable gains or an incomplete party must not produce an automatic recommendation.");
        Check(VoidCrystalPolicy.Rank(new[] { noParty }, "damage", 1).Single() == noParty,
            "A selected hero's own comparison does not require current party stats.");
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
        var changedRarity = Gear(1, 3, 60, 1); changedRarity.Rarity++;
        var changedEffect = Gear(1, 3, 60, 1); changedEffect.NativeEffect++;
        var changedPower = Gear(1, 3, 60, 1); changedPower.PowerGain++;
        Check(!VoidCrystalPolicy.CanSpend(old, changedRarity, 1000, 0, 1000) &&
              !VoidCrystalPolicy.CanSpend(old, changedEffect, 1000, 0, 1000) &&
              !VoidCrystalPolicy.CanSpend(old, changedPower, 1000, 0, 1000),
            "A changed rarity, effect or power must invalidate the pending enchant.");
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
        GameInitialize.battleCharactersHolder = new();
        Time.unscaledTime = 0;
        var game = new Game();
        var loader = GameInitialize.HandlerLoader;
        loader.currencyHandler.currencies[CurrencyCode.VoidCrystal] = game.Balance;
        loader.heroDeck.heroes = new[] { new Hero { code = 0 }, new Hero { code = 1 }, new Hero { code = 2, Unlocked = false } };
        loader.MainBattle.Fighting.UnionWith(new[] { 0, 1 });
        GameInitialize.battleCharactersHolder.heroesTeamLogic.Heroes = new[]
        {
            new HeroBattleLogic { battleHero = new BattleHero { hero = loader.heroDeck.heroes[0], DPS = 80, maximumHealthTotal = 40, armorTotal = 30 } },
            new HeroBattleLogic { battleHero = new BattleHero { hero = loader.heroDeck.heroes[1], DPS = 20, maximumHealthTotal = 60, armorTotal = 70 } },
        };
        foreach (var h in loader.heroDeck.heroes)
        {
            var server = new HeroServer { code = h.code };
            var slot = h.code == 0 ? GearSlot.Wrist : GearSlot.Relic;
            server.heroGears.Add(new GearItem { heroId = (HeroCode)h.code, gearSlot = slot,
                gearTier = slot == GearSlot.Relic ? GearTier.Tier3 : GearTier.Tier2 });
            server.heroGears.Add(new GearItem { heroId = (HeroCode)h.code, gearSlot = GearSlot.Weapon,
                gearTier = GearTier.Tier1, enchantCost = 30 });
            h.heroGearItems = server.heroGears;
            server.gearsDict[slot] = new GearItem { heroId = (HeroCode)h.code, gearSlot = slot, itemRarity = Rarity.None };
            server.gearsDict[GearSlot.Weapon] = new GearItem { heroId = (HeroCode)h.code, gearSlot = GearSlot.Weapon, itemRarity = Rarity.None };
            loader.heroServerHandler.heroesDict[(HeroCode)h.code] = server;
        }
        Button Button(string path) => GameObject.CreatePath(path).AddComponent<Button>();
        game.View.CategoryGearButton = Button("menus/HallOfHeroes/gear");
        game.View.CategoryGearObj = GameObject.CreatePath("menus/HallOfHeroes/items");
        Button("menus/HallOfHeroes/submenus/submenuButtons/enchanting");
        UnityEngine.Object.TestInstance = game.View;
        var mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        mech.heroInteractions = loader.heroDeck.heroes.Select(h => new HeroInteractionSquare
        {
            hero = loader.heroServerHandler.heroesDict[(HeroCode)h.code],
            actionOnClick = () => mech.SelectHero(h.code, false),
        }).ToList();
        mech.OnSelect = id =>
        {
            mech.heroServerSelected = loader.heroServerHandler.heroesDict[(HeroCode)id];
            game.View.GearItems = mech.heroServerSelected.heroGears.Select(gear =>
            {
                var button = Button("menus/HallOfHeroes/items/" + id + "/" + gear.gearSlot);
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
                return new EnchantGearItemInteraction { slot = gear.gearSlot, gearItem = gear, enchantGearItemButton = button };
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
        foreach (var hero in GameInitialize.HandlerLoader.heroServerHandler.heroesDict.Values)
            foreach (var gear in hero.heroGears)
                if (gear.gearSlot == GearSlot.Weapon) gear.enchantCost = 1000;
        var ring = new GearItem { heroId = HeroCode.Talia, gearSlot = GearSlot.Ring,
            gearTier = GearTier.Tier1, enchantCost = 180 };
        GameInitialize.HandlerLoader.heroServerHandler.heroesDict[HeroCode.Talia].heroGears.Add(ring);
        GameInitialize.MenuLoader.HallOfHeroesMech.OnSelect(0);
        Check(Snapshot().GetProperty("items").EnumerateArray().Any(i =>
            i.GetProperty("slot").GetInt32() == 6 &&
            i.GetProperty("values").GetProperty("balanced").GetProperty("all").GetDouble() > 0),
            "The live snapshot must export balanced priorities.");
        Check(VoidCrystalSpender.Request(Packet(240, "balanced")) == "Spending",
            "Balanced spending request rejected.");
        Pump();
        Check(VoidCrystalSpender.Ok && game.Clicked.Count == 2 && game.Clicked[0] == ring &&
              game.Clicked[1].gearSlot == GearSlot.Wrist && game.Balance.amount == 760,
            $"Balanced spending must re-rank after the ring: {VoidCrystalSpender.Status}; " +
            $"clicked {string.Join(", ", game.Clicked.Select(g => g.gearSlot))}; balance {game.Balance.amount}.");

        game = Setup();
        var options = VoidCrystalSpender.ReadOptions().Items;
        Check(options.Count == 4 && options.All(o => o.Hero != 2),
            "The live gear list must be used even when the dictionary contains empty placeholders; unowned gear must be excluded.");
        var snapshot = Snapshot();
        Check(snapshot.GetProperty("party").GetProperty("damage").GetBoolean() &&
              snapshot.GetProperty("items").EnumerateArray().Any(i =>
                  i.GetProperty("slot").GetInt32() == 0 &&
                  i.GetProperty("values").GetProperty("damage").GetProperty("all").GetDouble() > 0),
            "The live snapshot did not export party-weighted values for personal gear.");
        var packet = Packet();
        Check(VoidCrystalSpender.Request(packet) == "Spending", "Valid request rejected.");
        Pump();
        Check(!VoidCrystalSpender.Busy && VoidCrystalSpender.Ok && game.Clicked.Count == 3 && game.Balance.amount == 880,
            "Actual spending loop did not choose affordable upgrades within the exact budget.");
        Check(game.Clicked[0].heroId == HeroCode.Talia && game.Clicked[0].gearSlot == GearSlot.Weapon &&
              game.Clicked[1].heroId == HeroCode.Burt && game.Clicked[1].gearSlot == GearSlot.Relic &&
              game.Clicked[2].heroId == HeroCode.Burt && game.Clicked[2].gearSlot == GearSlot.Weapon,
            "Actual spending did not re-rank after upgrading the active hero's weapon.");
        Check(VoidCrystalSpender.Request(packet) != "Spending", "Completed request replayed.");

        game = Setup();
        Check(VoidCrystalSpender.Request(Packet(30, target: 0)) == "Spending", "Personal gear request rejected.");
        Pump();
        Check(game.Clicked.Count == 1 && game.Clicked[0].gearSlot == GearSlot.Weapon && game.Balance.amount == 970,
            "Personal mode did not buy the selected hero's weapon.");

        game = Setup();
        game.View.CategoryGearButton.gameObject.activeSelf = false;
        Check(VoidCrystalSpender.Request(Packet(30, target: 0)) == "Spending", "Hidden category-button scenario could not start.");
        Pump();
        Check(VoidCrystalSpender.Ok && game.Clicked.Count == 1 && game.Balance.amount == 970,
            "An already-visible gear category must work when the gear category button is hidden.");

        game = Setup();
        game.View.CategoryGearButton.gameObject.activeSelf = false;
        game.View.CategoryGearObj.activeSelf = false;
        Check(VoidCrystalSpender.Request(Packet(30, target: 0)) == "Spending", "Hidden gear-category scenario could not start.");
        Pump();
        Check(!VoidCrystalSpender.Ok && game.Clicked.Count == 0 && game.Balance.amount == 1000,
            "Spending must stop if the gear category itself is not visible.");

        game = Setup();
        game.View.GearItems.Single(i => i.slot == GearSlot.Weapon).enchantGearItemButton.enabled = false;
        Check(VoidCrystalSpender.Request(Packet(30, target: 0)) == "Spending", "Disabled enchant-button scenario could not start.");
        Pump();
        Check(!VoidCrystalSpender.Ok && game.Clicked.Count == 0 && game.Balance.amount == 1000,
            "Spending must stop if the enchant button is disabled.");

        game = Setup();
        var capped = LiveGear(HeroCode.Burt, GearSlot.Relic);
        capped.EnchantLevel = capped.MaxLevel;
        GameInitialize.HandlerLoader.heroServerHandler.heroesDict[HeroCode.Talia].gearTier = 1;
        Check(VoidCrystalPolicy.Rank(VoidCrystalSpender.ReadOptions().Items, "damage", -1)
                .All(i => i.Slot == (int)GearSlot.Weapon),
            "Maxed gear or a locked tier remained eligible.");

        game = Setup();
        foreach (var server in GameInitialize.HandlerLoader.heroServerHandler.heroesDict.Values)
        {
            server.gearTier = 0;
            foreach (var gear in server.heroGears) gear.MaxLevel = 0;
        }
        var fresh = VoidCrystalSpender.ReadOptions().Items;
        Check(fresh.Count == 2 && fresh.All(o => o.Slot == (int)GearSlot.Weapon),
            "A new account's tier 1 gear was hidden by a zero tier count or the game's per-rarity flag.");
        Check(VoidCrystalSpender.Skipped.StartsWith("2 upgradable") && VoidCrystalSpender.Skipped.Contains("2 in a locked tier"),
            "The gear scan summary did not explain skipped items.");

        game = Setup();
        var levelThree = LiveGear(HeroCode.Talia, GearSlot.Weapon);
        levelThree.itemRarity = Rarity.Rare;
        levelThree.EnchantLevel = 3;
        var levelThreeOption = VoidCrystalSpender.ReadOptions().Items.Single(i => i.Hero == 0 && i.Slot == 0);
        Check(Math.Abs(levelThreeOption.MultiplierGain - (15d / 8 - 1)) < 1e-12,
            "The live enchant table must supply the actual level 3 to 4 multiplier.");
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
                GameInitialize.MenuLoader.HallOfHeroesMech.heroServerSelected.heroGears.Single(g => g.gearSlot == GearSlot.Weapon).enchantCost = 31;
        };
        Pump();
        Check(changed.Clicked.Count == 0 && !VoidCrystalSpender.Ok, "A changed live price was spent.");
        Firebot.Behaviors.Extra.Ui.BeforeFind = null;

        var changedParty = Setup();
        Check(VoidCrystalSpender.Request(Packet(30)) == "Spending", "Party-change scenario could not start.");
        Firebot.Behaviors.Extra.Ui.BeforeFind = path =>
        {
            if (!path.EndsWith("/enchanting")) return;
            var heroes = GameInitialize.battleCharactersHolder.heroesTeamLogic.Heroes;
            heroes[0].battleHero.DPS = 10;
            heroes[1].battleHero.DPS = 90;
        };
        Pump();
        Check(changedParty.Clicked.Count == 1 && changedParty.Clicked[0].heroId == HeroCode.Burt,
            "Changed party contributions must re-rank before the click and select the new best hero.");
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

    public static void HeroSelectionSafety()
    {
        Game SparseRoster()
        {
            var game = Setup();
            var loader = GameInitialize.HandlerLoader;
            loader.heroDeck.heroes[1].code = 21;
            loader.MainBattle.Fighting.Remove(1);
            loader.MainBattle.Fighting.Add(21);
            var server = loader.heroServerHandler.heroesDict[HeroCode.Burt];
            loader.heroServerHandler.heroesDict.Remove(HeroCode.Burt);
            server.code = 21;
            foreach (var gear in server.heroGears) gear.heroId = (HeroCode)21;
            loader.heroServerHandler.heroesDict[(HeroCode)21] = server;
            var mech = GameInitialize.MenuLoader.HallOfHeroesMech;
            var tiles = mech.heroInteractions;
            mech.heroInteractions = new() { tiles[2], tiles[1], tiles[0] };
            return game;
        }

        var game = SparseRoster();
        var mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        Check(VoidCrystalSpender.Request(Packet(30, target: 21)) == "Spending", "Sparse-roster request rejected.");
        Pump();
        Check(VoidCrystalSpender.Ok && game.Clicked.Count == 1 && (int)game.Clicked[0].heroId == 21 &&
              mech.SelectedCodes.SequenceEqual(new[] { 21 }) && mech.ArrowCalls == 0,
            "Reordered tiles must select their bound unlocked hero ID, never the tile index or a locked hero.");

        game = SparseRoster();
        mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        var delayedTile = mech.heroInteractions.Single(t => t.hero.code == 21);
        mech.heroInteractions.Remove(delayedTile);
        Check(VoidCrystalSpender.Request(Packet(30, target: 21)) == "Spending", "Delayed-tile request rejected.");
        Pump(() =>
        {
            if (Time.unscaledTime >= 1 && !mech.heroInteractions.Contains(delayedTile))
                mech.heroInteractions.Add(delayedTile);
        });
        Check(VoidCrystalSpender.Ok && game.Clicked.Count == 1 && mech.SelectedCodes.Count == 1,
            "Hero selection must wait for lazy-loaded tiles and invoke the correct action only once.");

        game = SparseRoster();
        mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        mech.heroInteractions.RemoveAll(t => t.hero.code == 21);
        Check(VoidCrystalSpender.Request(Packet(30, target: 21)) == "Spending", "Missing-tile request rejected.");
        Pump();
        Check(!VoidCrystalSpender.Ok && !VoidCrystalSpender.Busy && game.Clicked.Count == 0 &&
              mech.SelectedCodes.Count == 0 && mech.ArrowCalls == 0 && mech.heroServerSelected.code == 0,
            "Missing hero tiles must stop safely without scanning into locked heroes.");

        game = SparseRoster();
        mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        mech.heroSelectionLoading = true;
        Check(VoidCrystalSpender.Request(Packet(30, target: 21)) == "Spending", "Loading-hero request rejected.");
        Pump(() =>
        {
            GameInitialize.HandlerLoader.heroDeck.heroes[1].Unlocked = false;
            mech.heroSelectionLoading = false;
        });
        Check(!VoidCrystalSpender.Ok && game.Clicked.Count == 0 && mech.SelectedCodes.Count == 0,
            "A hero that becomes locked before selection must never be selected or enchanted.");

        game = SparseRoster();
        mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        mech.heroInteractions.Single(t => t.hero.code == 21).actionOnClick = () => { };
        Check(VoidCrystalSpender.Request(Packet(30, target: 21)) == "Spending", "Unconfirmed-selection request rejected.");
        Pump();
        Check(!VoidCrystalSpender.Ok && game.Clicked.Count == 0 && mech.ArrowCalls == 0,
            "An unconfirmed hero action must not fall back to unsafe arrow navigation or spend crystals.");

        game = SparseRoster();
        mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        var tile = mech.heroInteractions.Single(t => t.hero.code == 21);
        var select = tile.actionOnClick;
        var loadingTicks = 0;
        tile.actionOnClick = () =>
        {
            select();
            mech.heroSelectionLoading = true;
            loadingTicks = 4;
        };
        Check(VoidCrystalSpender.Request(Packet(30, target: 21)) == "Spending", "Asynchronous selection rejected.");
        Pump(() => { if (loadingTicks > 0 && --loadingTicks == 0) mech.heroSelectionLoading = false; });
        Check(VoidCrystalSpender.Ok && game.Clicked.Count == 1 && mech.SelectedCodes.Count == 1,
            "Hero loading must finish before an enchant, without duplicate hero actions.");

        game = SparseRoster();
        mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        mech.heroInteractions.Single(t => t.hero.code == 21).actionOnClick = () =>
            mech.heroServerSelected = GameInitialize.HandlerLoader.heroServerHandler.heroesDict[HeroCode.Boris];
        Check(VoidCrystalSpender.Request(Packet(30, target: 21)) == "Spending", "Stale-binding selection rejected.");
        Pump();
        Check(!VoidCrystalSpender.Ok && game.Clicked.Count == 0 && mech.ArrowCalls == 0,
            "A tile action that selects the wrong hero must never allow spending.");

        game = SparseRoster();
        mech = GameInitialize.MenuLoader.HallOfHeroesMech;
        mech.heroSelectionLoading = true;
        Check(VoidCrystalSpender.Request(Packet(30, target: 21)) == "Spending", "Scene-change selection rejected.");
        Pump(() => GameInitialize.Loaded = false);
        Check(!VoidCrystalSpender.Ok && game.Clicked.Count == 0 && mech.SelectedCodes.Count == 0,
            "A scene change during hero loading must stop before selecting or spending.");
    }

    public static void RequestSafety()
    {
        Setup();
        foreach (var bad in new[] { "{}", "{", Packet(0), Packet(-1), Packet(1001), Packet(1.5), Packet(60, "invalid"), Packet(60, target: -2), Packet(60, target: 999) })
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
        LiveGear(HeroCode.Talia, GearSlot.Wrist).ThrowOnCostText = true;
        var snapshot = Snapshot();
        Check(!snapshot.GetProperty("available").GetBoolean() && snapshot.GetProperty("items").GetArrayLength() == 0,
            "Unreadable gear produced a success-shaped snapshot.");
        Check(VoidCrystalSpender.Request(Packet()) != "Spending", "Unreadable gear allowed spending.");
        Check(!VoidCrystalSpender.Busy, "Rejected request left busy set.");

        Setup();
        GameInitialize.battleCharactersHolder.heroesTeamLogic.Heroes = Array.Empty<HeroBattleLogic>();
        Check(!Snapshot().GetProperty("party").GetProperty("damage").GetBoolean(),
            "Missing battle heroes should be surfaced as unavailable party stats.");
        Check(VoidCrystalSpender.Request(Packet()) != "Spending", "Incomplete party stats allowed automatic combat spending.");
        Check(VoidCrystalSpender.Request(Packet(30, "power")) == "Spending", "Gear-power ranking should not depend on battle stats.");
        VoidCrystalSpender.Cancel();

        Setup();
        LiveGear(HeroCode.Talia, GearSlot.Weapon).EffectOverride = 123456;
        Check(!Snapshot().GetProperty("available").GetBoolean() && VoidCrystalSpender.Request(Packet()) != "Spending",
            "A native effect inconsistent with the live tables must fail closed.");

        Setup();
        foreach (var server in GameInitialize.HandlerLoader.heroServerHandler.heroesDict.Values)
            foreach (var item in server.heroGears) item.itemRarity = Rarity.None;
        var empty = Snapshot();
        Check(empty.GetProperty("available").GetBoolean() && empty.GetProperty("ownedGear").GetInt32() == 0 &&
              empty.GetProperty("items").GetArrayLength() == 0,
            "A new account without gear should explain the empty inventory rather than report an error.");
        GameInitialize.HandlerLoader.heroServerHandler.heroesDict[HeroCode.Talia].gearsRaw
            .Add(new GearServer { s = (int)GearSlot.Weapon, r = 1 });
        Check(!Snapshot().GetProperty("available").GetBoolean(),
            "Server-owned gear missing from the live list must not be reported as an empty inventory.");
    }
}
