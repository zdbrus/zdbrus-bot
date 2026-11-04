using System.Collections;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using ResearchQuantity = Il2Cpp.MeteoriteResearchPreviewPopup.ResearchQuantity;

internal static class MeteoriteTests
{
    private const string Library = "menus/Library";
    private const string Tree = Library + "/submenus/meteoriteResearch/submenus/tree1";
    private const string Preview = "popups/MeteoriteResearchPreview/bg";
    private const string Buy = Preview + "/unlocked/researchButton";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Game
    {
        public MeteoriteResearchTask Task = new();
        public Currency Balance = new();
        public MeteoriteResearch High = new() { index = 0, name = "Attribute damage", levelUpCost = 200 };
        public MeteoriteResearch Low = new() { index = 1, name = "Raining gold", levelUpCost = 100 };
        public MelonPreferences_Entry<int> Keep;
        public MeteoriteResearchPreviewPopup Popup = new();
        public List<string> Purchases = new();
        public Action<MeteoriteResearch> Purchase;
        public Action AfterYield;
        public int StartQuantity = 1;
        public ResearchQuantity StartMode = ResearchQuantity.ResearchX1;
        public int QuantityChanges;

        public void Run(IEnumerator routine = null)
        {
            routine ??= Task.Execute();
            var steps = 0;
            while (routine.MoveNext())
            {
                if (++steps > 2000) throw new InvalidOperationException("Research did not finish.");
                if (routine.Current is IEnumerator child) Run(child);
                Time.unscaledTime += 0.1f;
                AfterYield?.Invoke();
            }
        }
    }

    private static Game Setup(double balance = 1981, int keep = 1781)
    {
        GameObject.Clear();
        GameObject.BeforeFind = null;
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        Ui.BeforeFind = null;
        Ui.BeforeText = null;
        Ui.BeforeVisible = null;
        Ui.BeforeGuardedClick = null;
        Ui.MissingCurrency = false;
        Logger.Messages.Clear();
        Time.unscaledTime = 0;
        var g = new Game();
        g.Task.InitializeConfig("test");
        g.Keep = (MelonPreferences_Entry<int>)MelonPreferences.Categories["meteorite_research"]
            .Entries["keep_meteorites"];
        g.Keep.Value = keep;
        ((MelonPreferences_Entry<string>)MelonPreferences.Categories["meteorite_research"]
            .Entries["priority"]).Value = "Attribute damage,Raining gold";
        g.Balance.amount = balance;
        GameInitialize.HandlerLoader = new();
        GameInitialize.HandlerLoader.currencyHandler.currencies[CurrencyCode.Meteorite] = g.Balance;
        GameInitialize.MenuLoader = new() { MeteoriteResearchPreviewPopup = g.Popup };
        GameObject.CreatePath(Library + "/submenuButtons/meteoriteResearch").AddComponent<Button>();
        GameObject.CreatePath(Library + "/closeButton").AddComponent<Button>();
        GameObject.CreatePath(Preview + "/closeButton").AddComponent<Button>().onClick.Action =
            () => GameObject.Resolve(Preview).activeSelf = false;
        GameObject.CreatePath(Buy).AddComponent<Button>().onClick.Action = () =>
        {
            g.Purchases.Add(g.Popup.meteoriteResearch.name);
            Check(g.Popup.currentResearchQuantity == 1 &&
                  g.Popup.currentResearchQuantityEnum == ResearchQuantity.ResearchX1, "Bot clicked a bulk purchase.");
            if (g.Purchase != null) g.Purchase(g.Popup.meteoriteResearch);
            else
            {
                g.Balance.amount -= g.Popup.meteoriteResearch.levelUpCost.GetDecrypted();
                g.Popup.meteoriteResearch.level++;
            }
        };
        GameObject.CreatePath(Preview + "/unlocked/changeQuantityButton").AddComponent<Button>().onClick.Action = () =>
        {
            g.QuantityChanges++;
            g.Popup.meteoriteResearch.levelUpCost =
                g.Popup.meteoriteResearch.levelUpCost.GetDecrypted() / g.Popup.currentResearchQuantity;
            g.Popup.currentResearchQuantity = 1;
            g.Popup.currentResearchQuantityEnum = ResearchQuantity.ResearchX1;
        };
        foreach (var research in new[] { g.High, g.Low })
        {
            var node = GameObject.CreatePath(Tree + "/research" + research.index);
            node.AddComponent<MeteoriteResearchInteraction>().meteoriteResearch = research;
            node.AddComponent<Button>().onClick.Action = () =>
            {
                g.Popup.meteoriteResearch = research;
                g.Popup.currentResearchQuantity = g.StartQuantity;
                g.Popup.currentResearchQuantityEnum = g.StartMode;
                research.levelUpCost = research.levelUpCost.GetDecrypted() * g.StartQuantity;
                Ui.Texts[Preview + "/researchName"] = research.name;
                GameObject.Resolve(Preview).activeSelf = true;
            };
        }
        GameObject.Resolve(Preview).activeSelf = false;
        return g;
    }

    public static void Boundaries()
    {
        Check(PurchaseObservation.CanAffordWithReserve(1981, 200, 1781), "Exact boundary rejected.");
        Check(!PurchaseObservation.CanAffordWithReserve(1980, 200, 1781), "Reserve boundary exceeded.");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
        {
            Check(!PurchaseObservation.CanAffordWithReserve(invalid, 200, 1781), "Invalid balance accepted.");
            Check(!PurchaseObservation.CanAffordWithReserve(1981, invalid, 1781), "Invalid cost accepted.");
            Check(!PurchaseObservation.CanAffordWithReserve(1981, 200, invalid), "Invalid reserve accepted.");
        }
        Check(!PurchaseObservation.CanAffordWithReserve(1981, 0, 1781), "Unknown zero cost accepted.");
        foreach (var (balance, expected) in new[] { (1981d, 1), (1980d, 0), (2381d, 3), (1781d, 0) })
        {
            var g = Setup(balance);
            // Deliberately misleading rounded display values must never authorize spending.
            Ui.Numbers[Library + "/counters/currencyInteraction (Meteorite)/quantity"] = 2000;
            Ui.Texts[Library + "/counters/currencyInteraction (Meteorite)/quantity"] = "2.0K";
            Ui.Numbers[Buy + "/cost"] = 100;
            g.Run();
            Check(g.Purchases.Count == expected && g.Balance.amount >= 1781 &&
                  !g.Purchases.Contains(g.Low.name), "Exact reserve or priority was violated.");
        }
        var zero = Setup(400, 0);
        zero.Run();
        Check(zero.Purchases.Count == 2 && zero.Balance.amount == 0, "Zero reserve must permit the full balance.");
    }

    public static void LiveGuard()
    {
        foreach (var change in new Action<Game>[]
        {
            g => g.Keep.Value = 1782,
            g => g.Balance.amount = 1980,
            g => g.High.levelUpCost = 201,
            g => g.High.level++,
            g => g.Popup.meteoriteResearch = g.Low,
            g => g.Popup.meteoriteResearch = new MeteoriteResearch
                { index = g.High.index, name = g.High.name, levelUpCost = 400 },
            g => g.Popup.currentResearchQuantity = 5,
            g => g.Popup.currentResearchQuantityEnum = ResearchQuantity.ResearchX10,
            g => GameObject.Resolve(Preview).activeSelf = false,
        })
        {
            var g = Setup();
            Ui.BeforeGuardedClick = path => { if (path == Buy) change(g); };
            g.Run();
            Check(g.Purchases.Count == 0, "Changed state reached the purchase button.");
        }
        var raised = Setup(2381, 0);
        raised.Purchase = r =>
        {
            raised.Balance.amount -= r.levelUpCost.GetDecrypted();
            r.level++;
            raised.Keep.Value = 2181;
        };
        raised.Run();
        Check(raised.Purchases.Count == 1 && raised.Balance.amount == 2181,
            "Reserve changed during the pass was ignored.");
    }

    public static void Receipts()
    {
        foreach (var receipt in new Action<Game, MeteoriteResearch>[]
        {
            (g, r) => r.levelUpCost = 201,
            (g, r) => r.level++,
            (g, r) => g.Balance.amount -= 200,
            (g, r) => { r.level++; g.Balance.amount -= 201; },
        })
        {
            var g = Setup(2381);
            g.Purchase = r => receipt(g, r);
            g.Run();
            Check(g.Purchases.Count == 1 && Logger.Messages.Any(s => s.Contains("not confirmed")),
                "An uncertain receipt authorized another purchase.");
        }
        var delayed = Setup(2381);
        var debitAt = float.PositiveInfinity;
        delayed.Purchase = r => { r.level++; debitAt = Time.unscaledTime + 1; };
        delayed.AfterYield = () =>
        {
            if (Time.unscaledTime < debitAt) return;
            delayed.Balance.amount -= 200;
            debitAt = float.PositiveInfinity;
        };
        delayed.Run();
        Check(delayed.Purchases.Count == 3 && delayed.Balance.amount == 1781,
            "Delayed debit was not awaited before buying again.");
    }

    public static void QuantityAndPriorities()
    {
        var bulk = Setup();
        bulk.StartQuantity = 5;
        bulk.StartMode = ResearchQuantity.ResearchX5;
        bulk.Run();
        Check(bulk.Purchases.Count == 1 && bulk.QuantityChanges == 1 && bulk.Balance.amount == 1781,
            "Bulk selection was not normalized to one exact-cost level.");
        var stuck = Setup();
        stuck.StartQuantity = 5;
        stuck.StartMode = ResearchQuantity.ResearchX5;
        GameObject.Resolve(Preview + "/unlocked/changeQuantityButton").activeSelf = false;
        stuck.Run();
        Check(stuck.Purchases.Count == 0, "Unreadable bulk quantity was purchased.");
        var clamped = Setup();
        clamped.StartMode = ResearchQuantity.ResearchX5;
        clamped.Run();
        Check(clamped.Purchases.Count == 1 && clamped.QuantityChanges == 1,
            "A bulk option currently limited to one level was mistaken for fixed x1.");
        var capped = Setup(2381);
        capped.High.maxLevel = 2;
        capped.Run();
        Check(capped.Purchases.SequenceEqual(new[] { capped.High.name, capped.High.name,
                capped.Low.name, capped.Low.name }), "Lower priority ran before the higher priority was maxed.");
        var limited = Setup(10000);
        limited.High.maxLevel = 50;
        limited.Run();
        Check(limited.Purchases.Count == 10 && limited.Purchases.All(n => n == limited.High.name) &&
              limited.Task.NextRunTime < DateTime.Now.AddMinutes(3), "Per-pass cap released lower priorities.");
        var locked = Setup(1981);
        locked.High.Unlocked = false;
        locked.Run();
        Check(locked.Purchases.SequenceEqual(new[] { locked.Low.name, locked.Low.name }),
            "A locked research blocked available research.");
    }

    public static void Unreadable()
    {
        foreach (var change in new Action<Game>[]
        {
            g => g.Balance.amount = double.NaN,
            g => g.High.levelUpCost = double.NaN,
            g => GameInitialize.HandlerLoader.currencyHandler.currencies.Clear(),
            g => g.High.name = null,
            g => g.High.maxLevel = 0,
            g => g.Keep.Value = -1,
            g => GameInitialize.MenuLoader.MeteoriteResearchPreviewPopup = null,
        })
        {
            var g = Setup();
            change(g);
            g.Run();
            Check(g.Purchases.Count == 0, "Unreadable research state was purchased.");
        }
    }
}
