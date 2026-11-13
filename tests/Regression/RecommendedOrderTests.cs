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
        Check(talents[0] == "Battle cry" && talents[1] == "Fate", "Talents must lead with Battle cry and Fate.");
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
