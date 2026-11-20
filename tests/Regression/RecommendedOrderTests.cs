using System.Text.RegularExpressions;
using Firebot.Core;

internal static class RecommendedOrderTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string Registry()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UI", "Config", "ConfigRegistry.cs")))
            dir = dir.Parent;
        Check(dir != null, "ConfigRegistry.cs was not found above the test output folder.");
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "UI", "Config", "ConfigRegistry.cs"));
    }

    private static List<string> ChipIds(string registry, string key)
    {
        var start = registry.IndexOf($"[\"{key}\"] = new[]", StringComparison.Ordinal);
        Check(start >= 0, $"{key} has no chip list in ConfigRegistry.");
        var end = registry.IndexOf('}', start);
        return Regex.Matches(registry.Substring(start, end - start), "new ConfigChoice\\(\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
    }

    public static void MatchRegistry()
    {
        var registry = Registry();
        foreach (var (key, order) in RecommendedOrders.Adopted)
        {
            var names = RecommendedOrders.Split(order);
            var chips = ChipIds(registry, key);
            Check(names.Distinct().Count() == names.Count, $"{key}: the guide order repeats a name.");
            var unknown = names.Except(chips).ToList();
            Check(unknown.Count == 0, $"{key}: not a panel chip (exact case): {string.Join(", ", unknown)}");
            var missing = chips.Except(names).ToList();
            Check(missing.Count == 0, $"{key}: guide order leaves out {string.Join(", ", missing)}");
        }
        var talents = RecommendedOrders.Split(RecommendedOrders.Talents);
        Check(talents[0] == "Leader - Auto Abilities" && talents[1] == "Auto attacks",
            "Talents must lead with the two one-point auto-ability talents.");
        Check(talents.IndexOf("Guardian power") == talents.Count - 1, "Guide: Guardian power goes last.");

        // Names read from the live game (0.7.62 tree). A name the game does not use is never bought.
        var tree = new[] { "All main attributes", "Leadership", "Guardian power", "Team bonus",
            "Leader - Auto Abilities", "Attack speed", "Trainer Skills", "Critical chance", "Auto attacks", "Dodge",
            "Critical damage", "Librarian", "Meteorite hunter", "Expeditioner", "Powerless enemy", "Powerless boss",
            "Weaklings", "Expose Weakness", "Ancient Knowledge", "Raining gold", "Coworkers", "Twin dragons",
            "Battle cry", "Alchemy", "Fate", "Mana heroes", "Energy heroes", "Rage heroes", "Damage specialization",
            "Tank specialization", "Healer specialization", "Fist fight", "Precision", "Magic spells" };
        var notInGame = talents.Where(t => !tree.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        Check(notInGame.Count == 0, "Talent order has names the game does not use: " + string.Join(", ", notInGame));
        Check(tree.All(t => talents.Contains(t, StringComparer.OrdinalIgnoreCase)), "Talent order misses a game talent.");

        var personal = new[] { "Guardian power", "Hero level up cost", "Raining gold", "Firestone effect",
            "Tank specialization", "Damage specialization", "Healer specialization", "Fist fight", "Precision",
            "Magic spells", "Rage heroes", "Mana heroes", "Energy heroes", "Attribute armor", "Attribute health",
            "Attribute damage", "Firestone Finder", "All main attributes", "Miner", "Battle cry" };
        var tree2 = RecommendedOrders.Split(RecommendedOrders.PersonalTree);
        Check(tree2.OrderBy(x => x).SequenceEqual(personal.OrderBy(x => x)), "Personal tree order must use exactly the game's names.");
        Check(tree2[0] == "Battle cry" && tree2[1] == "Miner", "Guide: Battle cry and Miner lead the personal tree.");

        var exotic = RecommendedOrders.Split(RecommendedOrders.ExoticMerchant);
        Check(exotic[0] == "Firestone Finder" && exotic[1] == "Raining gold", "Guide: Firestone Finder, then Raining gold.");
        Check(RecommendedOrders.FirestoneResearch.StartsWith("7,14,16,1,") &&
              RecommendedOrders.FirestoneResearch.EndsWith(",8,9"), "Firestone research: Raining gold first, time cuts next, crit loot last.");
    }

    public static void TokenReserve()
    {
        var now = new DateTime(2026, 11, 15, 12, 0, 0);
        TokenNeed.Now = () => now;
        TokenNeed.Clear();
        Check(TokenNeed.Current == 0, "No reserve before a war machine asks.");
        TokenNeed.Request(1500);
        TokenNeed.Request(900);
        Check(TokenNeed.Current == 1500, "The largest request of a run is kept.");
        now = now.AddHours(7);
        Check(TokenNeed.Current == 0, "An old request expires so a stopped war machine task cannot hold tokens.");
        TokenNeed.Request(600);
        TokenNeed.Clear();
        Check(TokenNeed.Current == 0, "A new war machine run starts clean.");
        TokenNeed.Now = () => DateTime.Now;
    }

    public static void ChaosRiftRespectsWarMachineTokens()
    {
        var now = new DateTime(2026, 11, 19, 12, 0, 0);
        TokenNeed.Now = () => now;
        TokenNeed.Clear();
        Check(TokenNeed.AllowsSpend(2889, 2500), "With no war machine waiting the rift may spend.");
        TokenNeed.Request(1500);
        Check(!TokenNeed.AllowsSpend(2889, 2500), "A waiting war machine level must keep its tokens.");
        Check(TokenNeed.AllowsSpend(4000, 2500), "Tokens above the reserve may still be spent.");
        Check(!TokenNeed.AllowsSpend(double.NaN, 2500), "An unreadable balance never spends.");
        TokenNeed.Clear();
        TokenNeed.Now = () => DateTime.Now;
    }

    public static void ChestKeepOptions()
    {
        Check(ChestKeep.Options(1).Length == 0, "The last single chest is kept.");
        Check(ChestKeep.Options(5).SequenceEqual(new[] { "openx1" }), "A small last stack opens one at a time.");
        Check(ChestKeep.Options(25).SequenceEqual(new[] { "openx10", "openx1" }), "A big last stack opens by ten.");
        Check(ChestKeep.Options(11).All(o => o != "openxMax"), "Never open the whole last stack.");
    }

    public static void Chips()
    {
        var list = new List<string> { "A", "B" };
        RecommendedOrders.ApplyChip(list, "__clear", "__clear", "__rec", "X,Y");
        Check(list.Count == 0, "Clear must empty the list.");
        RecommendedOrders.ApplyChip(list, "__rec", "__clear", "__rec", " X , Y,X,, Z");
        Check(string.Join(",", list) == "X,Y,Z", "Recommended must restore the trimmed, de-duplicated default order.");
        RecommendedOrders.ApplyChip(list, "Y", "__clear", "__rec", "");
        RecommendedOrders.ApplyChip(list, "Y", "__clear", "__rec", "");
        Check(string.Join(",", list) == "X,Z,Y", "Ordinary chips still remove and re-add last.");
        Check(RecommendedOrders.Same("a, b", "A,B") && !RecommendedOrders.Same("a,b", "b,a"),
            "Order comparison ignores spacing and case, not order.");
    }
}
