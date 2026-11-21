using System.Collections;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

internal static class AutoPlayTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Run(IEnumerator routine)
    {
        var guarded = CoroutineGuard.Run(routine, "auto test");
        var steps = 0;
        while (guarded.MoveNext())
        {
            Time.unscaledTime += 0.1f;
            if (++steps > 2000) throw new InvalidOperationException("Unbounded auto task.");
        }
    }

    private static void Reset()
    {
        GameObject.Clear();
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        Ui.BeforeFind = null;
        Ui.BeforeText = null;
        Ui.BeforeGuardedClick = null;
        Ui.MissingCurrency = false;
        Logger.Messages.Clear();
        MelonPreferences.SavedValues.Clear();
        Il2CppHolydayStudios.Translator.Names.Clear();
        GameInitialize.MenuLoader = new();
        BotManager.IsRunning = true;
        NativeAutoPlay.Initialize(new HarmonyLib.Harmony("test"));
    }

    private static void Button(string path, Action action) =>
        GameObject.CreatePath(path).AddComponent<Button>().onClick.Action = action;

    private const string Character = P.Popups + "/Character/bg";
    private const string Tree = Character + "/submenus/talents/bg/talentTreeScroll/Viewport/content/talentTree";
    private const string Points = Character + "/submenus/talents/bg/talentPoints/container/pointInfo/talentsTextBg/talentsText";
    private const string Preview = P.Popups + "/TalentPreview/bg";
    private static readonly List<string> Previews = new();
    private static Talents _selected;
    private static int _saves;

    private static TalentsTask TalentFixture(string plan = "")
    {
        Reset();
        Previews.Clear();
        _saves = 0;
        Ui.Texts[Points] = "1/241";
        Button(Character + "/submenuButtons/talents", () => { });
        Button(Preview + "/closeButton", () => GameObject.Resolve(Preview).activeSelf = false);
        Button(Preview + "/talentUpgrades/upgradeTalentButton", () =>
        {
            _selected.tempLevel++;
            Ui.Texts[Preview + "/talentLevelText"] = $"Level {_selected.tempLevel}/25";
            Ui.Texts[Points] = "0/241";
        });
        GameObject.Resolve(Preview).activeSelf = false;
        const string unsaved = P.Popups + "/UnsavedChanges/bg";
        Button(Character + "/closeButton", () => GameObject.Resolve(unsaved).activeSelf = true);
        Button(unsaved + "/saveChangesButton", () =>
        {
            _saves++;
            GameObject.Resolve(unsaved).activeSelf = false;
            GameObject.Resolve(Character).activeSelf = false;
        });
        GameObject.Resolve(unsaved).activeSelf = false;
        for (var i = 0; i < 60; i++)
            Talent(i, "Completed " + i, true, 25);
        Talent(60, "Locked priority", false, 0);
        Talent(61, "Fallback", true, 0);
        Talent(62, "All main attributes", true, 0);
        var task = new TalentsTask();
        task.InitializeConfig("test.cfg");
        SetPlan(task, plan);
        return task;
    }

    private static void SetPlan(TalentsTask task, string plan)
    {
        var field = typeof(TalentsTask).GetField("_plan",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((MelonPreferences_Entry<string>)field!.GetValue(task)!).Value = plan;
    }

    private static Talents Node(int index) =>
        GameObject.Resolve(Tree + "/talentInteraction (" + index + ")").GetComponent<TalentsInteraction>().talent;

    /// <summary>Two copies of Battle cry and Alchemy; the plan must level the right copy to the right level.</summary>
    public static void TalentGuidePlan()
    {
        var task = TalentFixture("All main attributes#1:2,Battle cry#2:3,Alchemy#1:1");
        Talent(63, "Battle cry", true, 15);
        Talent(64, "Alchemy", true, 0);
        Talent(65, "Battle cry", true, 0);
        Talent(66, "Alchemy", false, 0);
        Ui.Texts[Points] = "10/241";
        Button(Preview + "/talentUpgrades/upgradeTalentButton", () =>
        {
            _selected.tempLevel++;
            Ui.Texts[Preview + "/talentLevelText"] = $"Level {_selected.tempLevel}/25";
            var left = int.Parse(Ui.Texts[Points].Split('/')[0]) - 1;
            Ui.Texts[Points] = left + "/241";
        });
        Run(task.Execute());
        Check(Node(62).tempLevel == 2, $"AMA should stop at 2, got {Node(62).tempLevel}.");
        Check(Node(65).tempLevel == 3, "Battle cry #2 should be leveled to 3.");
        // The plan gives Alchemy #1 one point; the 4 leftover points then follow the guide priority list,
        // where Alchemy is the first talent present in this fixture. The locked copy stays untouched.
        Check(Node(64).tempLevel == 5 && Node(66).tempLevel == 0, $"Alchemy #1 should end at 5, got {Node(64).tempLevel}.");
        Check(Node(63).tempLevel == 15 && Node(61).tempLevel == 0,
            "Leftover points should follow the priority list only after the plan is done.");
        Check(Previews.Take(3).SequenceEqual(new[] { "All main attributes", "Battle cry", "Alchemy" }), "Plan order was not followed.");
        Check(Ui.Texts[Points] == "0/241" && _saves == 1, "Points were not all spent and saved.");
    }

    /// <summary>A locked step holds every point; nothing leaks to the priority list.</summary>
    public static void TalentGuidePlanHolds()
    {
        var task = TalentFixture("All main attributes#1:1,Locked priority#1:5");
        Ui.Texts[Points] = "5/241";
        Button(Preview + "/talentUpgrades/upgradeTalentButton", () =>
        {
            _selected.tempLevel++;
            Ui.Texts[Preview + "/talentLevelText"] = $"Level {_selected.tempLevel}/25";
            var left = int.Parse(Ui.Texts[Points].Split('/')[0]) - 1;
            Ui.Texts[Points] = left + "/241";
        });
        Run(task.Execute());
        Check(Node(62).tempLevel == 5, $"Filler should top up the earlier guide talent, got {Node(62).tempLevel}.");
        Check(Node(61).tempLevel == 0 && Ui.Texts[Points] == "0/241", "Points leaked past a locked guide step.");

        task = TalentFixture("Battle cry#1:1");
        Talent(63, "Battle cry", false, 0);
        Talent(64, "Battle cry", true, 0);
        Run(task.Execute());
        Check(Previews.Count == 0 && Ui.Texts[Points] == "1/241", "Out-of-order copies must pause the plan.");

        task = TalentFixture("Missing talent#1:3,All main attributes#1:1");
        Talent(63, "Battle cry", false, 0);
        Run(task.Execute());
        Check(Node(62).tempLevel == 1, "A step whose name is not in the tree should be skipped, not hold the plan.");

        task = TalentFixture("not a step");
        Run(task.Execute());
        Check(Previews.Count == 0 && Ui.Texts[Points] == "1/241", "A malformed plan must not spend points.");
    }

    /// <summary>A guide one point short of a tier threshold gets a filler point and then carries on.</summary>
    public static void TalentGuideFillerOpensTier()
    {
        var task = TalentFixture("All main attributes#1:2,Battle cry#1:3");
        Talent(63, "Battle cry", false, 0);
        Ui.Texts[Points] = "6/241";
        Button(Preview + "/talentUpgrades/upgradeTalentButton", () =>
        {
            _selected.tempLevel++;
            Ui.Texts[Preview + "/talentLevelText"] = $"Level {_selected.tempLevel}/25";
            var left = int.Parse(Ui.Texts[Points].Split('/')[0]) - 1;
            Ui.Texts[Points] = left + "/241";
            if (Node(62).tempLevel >= 3) Node(63).unlocked = true;
        });
        Run(task.Execute());
        Check(Node(62).tempLevel == 3 && Node(63).tempLevel == 3,
            $"Expected AMA 3 (one filler) and Battle cry 3, got {Node(62).tempLevel} and {Node(63).tempLevel}.");
    }

    public static void RunNowClearsTimers()
    {
        var task = TalentFixture();
        task.RestoreNextRun(DateTime.Now.AddHours(5));
        task.BubbleQuietUntil = DateTime.Now.AddHours(1);
        task.BubbleIdleStreak = 3;
        Check(task.DueTime > DateTime.Now, "Fixture should start not due.");
        task.RequestRunNow();
        Check(task.DueTime <= DateTime.Now && task.BubbleQuietUntil <= DateTime.Now && task.BubbleIdleStreak == 0,
            "Run now must make the task due immediately and clear the bubble cooldown.");
    }

    public static void TalentPlanParsing()
    {
        Check(TalentPlan.TryParse(TalentPlan.Guide, out var steps, out var error), "Guide plan did not parse: " + error);
        Check(steps.Count == 39, $"Guide should have 39 steps, has {steps.Count}.");
        Check(steps[0] == new TalentPlan.Step("All main attributes", 1, 9), "First guide step is wrong.");
        Check(steps[^1] == new TalentPlan.Step("Battle cry", 2, 15), "Last guide step is wrong.");
        Check(TalentPlan.TryParse("Leader - Auto Abilities:1", out steps, out _) && steps[0].Copy == 1 &&
              steps[0].Name == "Leader - Auto Abilities", "Copy should default to 1 and keep hyphenated names.");
        Check(!TalentPlan.TryParse("Alchemy#0:5", out _, out _) && !TalentPlan.TryParse("Alchemy", out _, out _),
            "Malformed steps must be rejected.");
        Check(TalentPlan.NodeOrder("talentInteraction (12)", 3) == 12 && TalentPlan.NodeOrder("talentInteraction", 0) == 0,
            "Node order parsing is wrong.");
        Check(TalentPlan.CopiesConsistent(new[] { true, true, false }) &&
              !TalentPlan.CopiesConsistent(new[] { false, true }), "Copy unlock-order check is wrong.");
        Check(TalentPlan.Resolve("Party - Auto Abilities", new[] { "Leader - Auto Abilities", "Party auto-abilities" }) ==
              "Party auto-abilities", "Punctuation differences should still match.");
        Check(TalentPlan.Resolve("Party - Auto Abilities", new[] { "Leader - Auto Abilities", "Party - Auto Skills" }) == null,
            "Unrelated names must not match.");
        Check(TalentPlan.Resolve("Tea bonus", new[] { "Team bonus", "Tea bonus extra" }) == "Tea bonus extra" ||
              TalentPlan.Resolve("Tea bonus", new[] { "Team bonus" }) == null, "Loose match must be unambiguous.");
        var names = new[] { "All main attributes", "Leadership", "Leader - Auto Abilities", "Trainer skills",
            "Auto attacks", "Critical damage", "Critical chance", "Meteorite hunter", "Powerless boss",
            "Expose weakness", "Ancient knowledge", "Raining gold", "Coworkers", "Librarian", "Expeditioner",
            "Twin dragons", "Battle cry", "Alchemy", "Team bonus" };
        TalentPlan.TryParse(TalentPlan.Guide, out steps, out _);
        var unknown = steps.Select(s => s.Name).Where(n => !names.Contains(n)).Distinct().ToList();
        Check(unknown.Count == 0, "Guide uses unknown talent names: " + string.Join(", ", unknown));
    }

    private static void Talent(int index, string name, bool unlocked, int level)
    {
        var path = Tree + "/talentInteraction (" + index + ")";
        var term = "cat1/" + name.ToLowerInvariant().Replace(' ', '_') + "_";
        Il2CppHolydayStudios.Translator.Names[term] = name;
        var talent = new Talents { name = term, tempLevel = level, unlocked = unlocked };
        GameObject.CreatePath(path).AddComponent<TalentsInteraction>().talent = talent;
        Button(path, () =>
        {
            Previews.Add(name);
            _selected = talent;
            GameObject.Resolve(Preview).activeSelf = true;
            Ui.Texts[Preview + "/talentName"] = Il2CppHolydayStudios.Translator.Translate(talent.name);
            Ui.Texts[Preview + "/talentLevelText"] = $"Level {talent.tempLevel}/25";
        });
    }

    public static void TalentTargeting()
    {
        Run(TalentFixture().Execute());
        Check(Previews.SequenceEqual(new[] { "All main attributes" }), "Scanned irrelevant or completed previews.");
        Check(Ui.Texts[Points] == "0/241" && _saves == 1, "Selected point was not spent and saved.");
    }

    public static void TalentSafety()
    {
        var task = TalentFixture();
        GameObject.Resolve(Tree + "/talentInteraction (0)").GetComponent<TalentsInteraction>().talent = null;
        Run(task.Execute());
        Check(Previews.Count == 0 && Ui.Texts[Points] == "1/241", "Unreadable data caused fallback spending.");
        task = TalentFixture();
        Ui.BeforeGuardedClick = path =>
        {
            if (path?.EndsWith("talentInteraction (62)") == true)
                GameObject.Resolve(path).GetComponent<TalentsInteraction>().talent.name = "Different talent";
        };
        Run(task.Execute());
        Check(Previews.Count == 0 && Ui.Texts[Points] == "1/241", "Changed node was clicked.");
        Ui.BeforeGuardedClick = null;
    }

    private const string A = P.Menus + "/Awakening";
    private const string Awaken = A + "/awakeningOptions/awakenBg/awakenButton";
    private const string Crystals = A + "/counters/currencyInteraction (ArcaneCrystal)/quantity";

    public static void AwakeningBatches()
    {
        foreach (var quantity in new[] { 1, 2, 5, 10, 20 })
        {
            Reset();
            var mech = GameInitialize.MenuLoader.AwakeningMech;
            Ui.Numbers[Crystals] = 41704;
            Ui.Numbers[Awaken + "/costText"] = 500;
            GameObject.CreatePath(Awaken + "/arcaneCrystalIcon");
            var selector = A + "/awakeningOptions/" +
                           (quantity == 20 ? "awakeningQuantityGrid1" : "awakeningQuantityGrid") +
                           "/changeQuantityButton" + quantity;
            Button(selector, () =>
            {
                mech.awakeningQuantity = quantity;
                Ui.Numbers[Awaken + "/costText"] = quantity * 500;
            });
            var manualClicks = 0;
            Button(Awaken, () => manualClicks++);
            Button(A + "/closeButton", () => GameObject.Resolve(A).activeSelf = false);
            Button(A + "/autoAwakenToggle", () =>
            {
                mech.AutoAwakenToggle();
                for (var i = 0; i < 100 && mech.autoAwakenOn; i++)
                {
                    Il2CppSystem.Collections.IEnumerator denied = null;
                    if (!NativeAutoPlay.AwakeningPrefix(ref denied)) break;
                    Ui.Numbers[Crystals] -= Ui.Numbers[Awaken + "/costText"];
                }
            });
            MelonPreferences.SavedValues["awaken_heroes.quantity"] = quantity;
            MelonPreferences.SavedValues["awaken_heroes.enabled"] = true;
            var task = new AwakenHeroesTask();
            task.InitializeConfig("test.cfg");
            Run(task.Execute());
            Check(Ui.Numbers[Crystals] == 41704 % (500 * quantity), $"Incorrect spending for x{quantity}.");
            Check(!mech.autoAwakenOn && manualClicks == 0, "Auto was left running or manually clicked each batch.");
            Check(Logger.Messages.Any(m => m.Contains($"at x{quantity}")), "Missing confirmed batch result.");
            Check(task.DueTime == task.NextRunTime, "An extra bubble fallback delayed a scheduled Auto visit.");
        }
        Check(!AwakenHeroesTask.ValidQuantity(3) && !AwakenHeroesTask.ValidQuantity(0), "Invalid quantity accepted.");
    }

    public static void AutoSafety()
    {
        Reset();
        var balance = 1000d;
        var receipt = new AutoPlayReceipt();
        receipt.Expect(() => balance, 1000, 500, 1);
        var stopped = 0;
        using (var run = new NativeAutoPlay(AutoPlayKind.Awakening, _ => receipt.Observe(), () => stopped++))
        {
            Check(!run.Allow(default) && stopped == 1, "A second request bypassed an unconfirmed receipt.");
        }
        balance = 500;
        Check(receipt.Observe() && receipt.Confirmed == 1 && !receipt.Pending, "Exact receipt was not confirmed.");
        receipt.Expect(() => -1, 500, 500, 1);
        Check(!receipt.Observe() && receipt.Invalid, "Unreadable balance was treated as spent.");
        foreach (var invalid in new[] { -1d, double.NaN, double.PositiveInfinity, 0.5 })
            Check(!AutoPlayReceipt.Affordable(invalid, 1), "Invalid balance allowed spending.");
        var autoOn = true;
        var lease = new NativeAutoPlay(AutoPlayKind.Awakening, _ => true, () => autoOn = false);
        lease.Dispose();
        Check(!autoOn && lease.Stopped && !lease.Allow(default), "Cancellation did not stop native Auto.");
    }

    public static void ScarabBoundaries()
    {
        foreach (var allowPharaoh in new[] { false, true })
        {
            Reset();
            const string h = P.Menus + "/ScarabGame/helpCanvas";
            const string play = h + "/playButton";
            const string noble = h + "/counters/currencyInteraction (NoblesToken)/quantity";
            const string pharaoh = h + "/counters/currencyInteraction (PharaohsToken)/quantity";
            var mech = GameInitialize.MenuLoader.ScarabGameMech;
            Ui.Numbers[noble] = 2;
            Ui.Numbers[pharaoh] = 3;
            Ui.Numbers[play + "/layout/costText1"] = 1;
            Ui.Numbers[play + "/layout/costText2"] = 1;
            var nobleIcon = GameObject.CreatePath(play + "/layout/nobleTokenIcon");
            var pharaohIcon = GameObject.CreatePath(play + "/layout/pharaohTokenIcon");
            void Selection()
            {
                nobleIcon.activeSelf = Ui.Numbers[noble] > 0;
                pharaohIcon.activeSelf = !nobleIcon.activeSelf && Ui.Numbers[pharaoh] > 0;
                mech.currentPlayTokenSelection = new PlayTokenSelection
                {
                    SelectedCurrency = nobleIcon.activeSelf ? CurrencyCode.NoblesToken : CurrencyCode.PharaohsToken,
                    AvailableTokens = Ui.Numbers[nobleIcon.activeSelf ? noble : pharaoh],
                    TokensNeeded = 1, HasEnoughTokens = nobleIcon.activeSelf || pharaohIcon.activeSelf
                };
            }
            Selection();
            Button(play, () => throw new InvalidOperationException("Unexpected manual spin."));
            Button(h + "/bottomRightUI/autoPlayToggle", () =>
            {
                mech.SetAutoPlayState(true);
                for (var i = 0; i < 10 && mech.autoPlayOn; i++)
                {
                    Il2CppSystem.Collections.IEnumerator denied = null;
                    if (!NativeAutoPlay.ScarabStartPrefix(mech, ref denied)) break;
                    if (!NativeAutoPlay.ScarabPrefix(mech.currentPlayTokenSelection, ref denied)) break;
                    Ui.Numbers[nobleIcon.activeSelf ? noble : pharaoh]--;
                    Selection();
                }
            });
            using var auto = new ScarabAutoPlay(() => true, () => allowPharaoh);
            Run(auto.Execute());
            Check(Ui.Numbers[noble] == 0 && Ui.Numbers[pharaoh] == (allowPharaoh ? 0 : 3),
                "Native Auto crossed the Pharaoh permission boundary.");
            Check(!mech.autoPlayOn, "Scarab Auto remained enabled after the task.");
        }
        Reset();
        var engine = GameInitialize.MenuLoader.ScarabGameMech;
        engine.currentPlayTokenSelection = new PlayTokenSelection
        {
            SelectedCurrency = CurrencyCode.NoblesToken, TokensNeeded = 1, HasEnoughTokens = true
        };
        var stop = false;
        using var guard = new NativeAutoPlay(AutoPlayKind.Scarab, _ => true, () => stop = true);
        Il2CppSystem.Collections.IEnumerator result = null;
        Check(NativeAutoPlay.ScarabStartPrefix(engine, ref result), "Initial valid selection rejected.");
        var changed = engine.currentPlayTokenSelection;
        changed.SelectedCurrency = CurrencyCode.PharaohsToken;
        Check(!NativeAutoPlay.ScarabPrefix(changed, ref result) && stop && !result.MoveNext(),
            "Changed native currency was not blocked before its roll sequence.");
    }

    public static void Navigation()
    {
        Check(TaskNavigation.HubFor("AwakenHeroesTask") == TaskHub.Guild, "Awakening lost its guild route.");
        Check(TaskNavigation.HubFor("ScarabGameTask") == TaskHub.Town, "Scarab lost its town route.");
        Check(TaskNavigation.HubFor("BossRetryTask") == TaskHub.None, "Battle task retained a hub.");
        Check(TaskNavigation.KeepOpen("root/menus/TownIrongard/closeButton", TaskHub.Town), "Town was not retained.");
        Check(!TaskNavigation.KeepOpen("root/menus/TownIrongard/closeButton", TaskHub.Guild), "Wrong hub retained.");
        Check(!TaskNavigation.KeepOpen("root/popups/Rewards/bg/closeButton", TaskHub.Town), "Popup cleanup skipped.");
        Check(!TaskNavigation.KeepOpen("root/menus/ScarabGame/closeButton", TaskHub.Town), "Finished activity retained.");
    }
}
