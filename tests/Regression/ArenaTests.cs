using System.Collections;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Firebot.Planner;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

internal static class ArenaTests
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Run(IEnumerator routine)
    {
        var steps = 0;
        while (routine.MoveNext())
        {
            if (++steps > 2000) throw new InvalidOperationException("Arena coroutine did not finish.");
            if (routine.Current is IEnumerator child) Run(child);
            Time.unscaledTime += 0.1f;
        }
    }

    public static void PowerParsing()
    {
        foreach (var (text, expected) in new[]
                 {
                     ("40,341", 40341d), ("1.5K", 1500d), ("2 M", 2000000d),
                     ("3B", 3000000000d), ("4T", 4000000000000d)
                 })
        {
            Check(ArenaPowerPolicy.TryParsePower(text, out var actual) && actual == expected,
                $"Valid power {text} was rejected.");
        }
        foreach (var text in new[]
                 {
                     "", "0", "-1", "+1", "NaN", "Infinity", "1.5aa", "1.2.3",
                     "1,23", "1e9", "2/5", new string('9', 400)
                 })
            Check(!ArenaPowerPolicy.TryParsePower(text, out _), $"Invalid power {text} was accepted.");

        Check(ArenaPowerPolicy.TryParseFormationPower("Arena power: 40,341", out var formation) &&
              formation == 40341, "Observed formation power was rejected.");
        Check(ArenaPowerPolicy.TryParseFormationPower("1,000", out formation) && formation == 1000,
            "Plain formation power was rejected.");
        foreach (var text in new[] { "power: -1", "power: 5aa", "level 9: 5", "power 1,000", ":100" })
            Check(!ArenaPowerPolicy.TryParseFormationPower(text, out _), $"Invalid formation {text} was accepted.");
        Check(ArenaPowerPolicy.TryParseRank("1,234", out var rank) && rank == 1234, "Valid rank was rejected.");
        foreach (var text in new[] { "", "0", "-1", "1,23", "1st", "NaN" })
            Check(!ArenaPowerPolicy.TryParseRank(text, out _), $"Invalid rank {text} was accepted.");
        Check(ArenaPowerPolicy.TryParseTokens("4/5", out var tokens) && tokens == 4, "Token count was rejected.");
        Check(ArenaPowerPolicy.TryParseTokens("0", out tokens) && tokens == 0, "Zero tokens were rejected.");
        foreach (var text in new[] { "", "-1/5", "4/0", "4.5", "4/5 extra", "NaN", "1e5" })
            Check(!ArenaPowerPolicy.TryParseTokens(text, out _), $"Invalid token count {text} was accepted.");

        Check(ArenaPowerPolicy.WithinLimit(1000, 1000, 100), "Threshold equality was rejected.");
        Check(!ArenaPowerPolicy.WithinLimit(1000, 1001, 100), "Above-threshold power was accepted.");
        Check(ArenaPowerPolicy.WithinLimit(1000, 1500, 150), "Configured 150% boundary was rejected.");
        Check(!ArenaPowerPolicy.WithinLimit(double.NaN, 1, 100) &&
              !ArenaPowerPolicy.WithinLimit(1000, double.PositiveInfinity, 100) &&
              !ArenaPowerPolicy.WithinLimit(1000, 1, 0), "Invalid comparison was accepted.");
    }

    public static void Selection()
    {
        var f = Setup();
        f.SetOpponent(0, "Unreadable", 100, "1.5aa");
        f.SetOpponent(1, "Medium", 101, "900");
        f.SetOpponent(2, "Weakest", 102, "800");
        Run(f.Task.Execute());
        Check(f.Selected == "Weakest" && f.FightClicks == 1, "Did not choose the weakest valid opponent.");
        Check(Logger.Messages.Any(s => s.Contains("Weakest completed: won")),
            "The observed win result was not reported.");

        f = Setup();
        f.SetOpponent(0, "Equal", 100, "1,000");
        f.SetOpponent(1, "Above", 101, "1,001");
        f.SetOpponent(2, "Higher", 102, "2,000");
        Run(f.Task.Execute());
        Check(f.Selected == "Equal" && f.FightClicks == 1, "Equal power was not accepted at 100%.");

        f = Setup();
        f.IntSetting("max_opponent_power_percent").Value = 150;
        f.SetText(Fixture.Formation, "Arena power: 40,341");
        f.SetText(Fixture.AttackerPower, "40,341");
        f.SetOpponent(0, "A", 817, "63,038");
        f.SetOpponent(1, "B", 874, "53,939");
        f.SetOpponent(2, "C", 905, "59,463");
        Run(f.Task.Execute());
        Check(f.Selected == "B" && f.FightClicks == 1, "Observed Arena power formats were not usable.");

        foreach (var own in new[] { "", "Arena power: 0", "Arena power: 1.5aa", "Arena power: NaN" })
        {
            f = Setup();
            f.SetText(Fixture.Formation, own);
            Run(f.Task.Execute());
            Check(f.PreviewClicks == 0 && f.FightClicks == 0, $"Invalid own power {own} led to a fight.");
        }
        f = Setup();
        f.IntSetting("max_opponent_power_percent").Value = 0;
        Run(f.Task.Execute());
        Check(f.PreviewClicks == 0, "An invalid power limit led to a preview.");

        f = Setup();
        f.SetOpponent(0, "A", 100, "unknown");
        f.SetOpponent(1, "B", 101, "-2");
        f.SetOpponent(2, "C", 102, "0");
        Run(f.Task.Execute());
        Check(f.PreviewClicks == 0 && f.FightClicks == 0, "Unreadable opponents led to a fight.");

        f = Setup();
        f.SetText(Fixture.TokensPath, "unknown");
        Run(f.Task.Execute());
        Check(f.PreviewClicks == 0, "Unreadable tokens led to a fight.");
    }

    public static void PreviewSafety()
    {
        var f = Setup();
        f.Preview.activeSelf = true;
        Run(f.Task.Execute());
        Check(f.Preview.activeSelf && f.PreviewClicks == 0 && f.FightClicks == 0,
            "A pre-existing preview was changed.");

        f = Setup();
        f.Result.activeSelf = true;
        Run(f.Task.Execute());
        Check(f.Result.activeSelf && f.PreviewClicks == 0, "A pre-existing result was changed.");

        f = Setup();
        f.OpenPreview = false;
        Run(f.Task.Execute());
        Check(f.PreviewClicks == 1 && f.FightClicks == 0, "A missing preview led to a fight.");

        f = Setup();
        f.OnPreview = () => f.SetText(Fixture.DefenderName, "Someone else");
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "A mismatched opponent name led to a fight.");

        f = Setup();
        f.OnPreview = () => f.SetText(Fixture.DefenderRank, "999");
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "A mismatched opponent rank led to a fight.");

        f = Setup();
        f.OnPreview = () => f.SetText(Fixture.DefenderPower, "899");
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "A changed opponent power led to a fight.");

        f = Setup();
        f.OnPreview = () => f.SetText(Fixture.AttackerPower, "0");
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "An invalid preview squad power led to a fight.");

        f = Setup();
        f.OnPreview = () => f.SetText(Fixture.AttackerPower, "500");
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "An opponent over the preview squad's limit was fought.");

        f = Setup();
        f.FightButton.interactable = false;
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "A disabled fight button was used.");

        f = Setup();
        Ui.BeforeGuardedClick = path =>
        {
            if (path == Fixture.PreviewFight) f.SetText(Fixture.DefenderName, "Changed after preview");
        };
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "The preview was not rechecked at the fight click.");

        f = Setup();
        Ui.BeforeGuardedClick = path =>
        {
            if (path == Fixture.PreviewFight) f.IntSetting("max_opponent_power_percent").Value = 0;
        };
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "A changed invalid limit was not rechecked.");

        f = Setup();
        Ui.BeforeGuardedClick = path =>
        {
            if (path == f.Card(1) + "/fightButton") f.Preview.activeSelf = true;
        };
        Run(f.Task.Execute());
        Check(f.Preview.activeSelf && f.PreviewClicks == 0 && f.FightClicks == 0,
            "A preview opened just before selection was modified.");

        f = Setup();
        Ui.BeforeGuardedClick = path =>
        {
            if (path == f.Card(1) + "/fightButton") f.SetText(f.Card(1) + "/powerBg/powerValue", "999");
        };
        Run(f.Task.Execute());
        Check(f.PreviewClicks == 0 && f.FightClicks == 0, "A changed opponent card was clicked.");

        f = Setup();
        Ui.BeforeGuardedClick = path =>
        {
            if (path == Fixture.PreviewFight) f.Result.activeSelf = true;
        };
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "A result opened before the fight click was ignored.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        Ui.BeforeGuardedClick = path =>
        {
            if (path == Fixture.PreviewFight) SquadApplier.Active = 3;
        };
        Run(f.Task.Execute());
        Check(f.FightClicks == 0 && SquadApplier.Active == 1,
            "A changed squad was not rejected and restored before fighting.");
    }

    public static void DailyException()
    {
        var f = Setup(new DateTime(2026, 10, 30, 20, 15, 0));
        SetAboveLimit(f);
        Run(f.Task.Execute());
        Check(f.Selected == "Lowest" && f.FightClicks == 1, "The daily exception did not pick the weakest.");
        Check(f.StringSetting("last_forced_fight").Value == "2026-10-30" && f.Category.SaveCalls == 2,
            "The daily attempt was not persisted before the fight.");
        Run(f.Task.Execute());
        Check(f.FightClicks == 1, "The daily exception was used twice.");

        f = Setup(new DateTime(2026, 10, 30, 19, 59, 0));
        SetAboveLimit(f);
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "The daily exception ran before 20:00.");

        f = Setup(new DateTime(2026, 10, 30, 20, 0, 0));
        SetAboveLimit(f);
        f.BoolSetting("one_fight_daily").Value = false;
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "The disabled daily exception ran.");

        f = Setup(new DateTime(2026, 10, 30, 20, 0, 0));
        SetAboveLimit(f);
        f.SetOpponent(2, "Unknown", 102, "bad");
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "The daily exception used an incomplete opponent list.");

        f = Setup(new DateTime(2026, 10, 30, 20, 0, 0));
        SetAboveLimit(f);
        GameObject.Resolve(f.Card(2)).activeSelf = false;
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "The daily exception ran before the opponent list was complete.");

        f = Setup(new DateTime(2026, 10, 30, 20, 0, 0));
        SetAboveLimit(f);
        f.RefreshButton.gameObject.activeSelf = false;
        Run(f.Task.Execute());
        Check(f.FightClicks == 0, "An unavailable refresh control was mistaken for a disabled refresh.");

        f = Setup(new DateTime(2026, 10, 30, 20, 0, 0));
        SetAboveLimit(f);
        f.RefreshButton.interactable = true;
        Run(f.Task.Execute());
        Check(f.FightClicks == 0 && f.RefreshClicks == 1, "The daily exception bypassed an available refresh.");

        f = Setup(new DateTime(2026, 10, 30, 20, 0, 0));
        SetAboveLimit(f);
        f.OnPreview = () => f.SetText(Fixture.DefenderName, "Mismatch");
        Run(f.Task.Execute());
        Check(f.FightClicks == 0 && f.StringSetting("last_forced_fight").Value == "" && f.Category.SaveCalls == 1,
            "An unverified preview consumed the daily exception.");

        f = Setup(new DateTime(2026, 10, 30, 20, 0, 0));
        SetAboveLimit(f);
        f.ThrowOnFight = true;
        Run(f.Task.Execute());
        Check(f.FightClicks == 1 && f.StringSetting("last_forced_fight").Value == "2026-10-30",
            "An uncertain fight attempt was not recorded.");
        Run(f.Task.Execute());
        Check(f.FightClicks == 1, "An uncertain forced attempt was repeated.");

        f = Setup(new DateTime(2026, 10, 30, 20, 0, 0));
        SetAboveLimit(f);
        f.Category.FailSave = true;
        Run(f.Task.Execute());
        Check(f.FightClicks == 0 && f.StringSetting("last_forced_fight").Value == "",
            "A forced fight ran when its daily record could not be saved.");
    }

    public static void Refresh()
    {
        var f = Setup();
        SetAboveLimit(f);
        f.RefreshButton.interactable = true;
        f.OnRefresh = () =>
        {
            f.RefreshButton.interactable = false;
            f.SetOpponent(1, "New weak opponent", 201, "500");
        };
        Run(f.Task.Execute());
        Check(f.RefreshClicks == 1 && f.Selected == "New weak opponent" && f.FightClicks == 1,
            "A changed refresh list was not used.");

        f = Setup();
        SetAboveLimit(f);
        f.RefreshButton.interactable = true;
        Run(f.Task.Execute());
        Check(f.RefreshClicks == 1 && f.PreviewClicks == 0 && f.FightClicks == 0,
            "An unchanged refresh was treated as a fresh opponent list.");
    }

    public static void UnconfirmedBattle()
    {
        var f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        f.ShowResult = false;
        Run(f.Task.Execute());
        Check(f.FightClicks == 1 && f.PreviewClicks == 1 && SquadApplier.Active == 1,
            "An unconfirmed result led to another fight or left the wrong squad active.");
        Check(Logger.Messages.Any(s => s.Contains("no Arena result was observed")),
            "An unconfirmed result was not reported.");

        f = Setup();
        f.Transition = false;
        Run(f.Task.Execute());
        Check(f.FightClicks == 1 && !f.Preview.activeSelf &&
              Logger.Messages.Any(s => s.Contains("did not produce a confirmed transition")),
            "An unconfirmed fight transition was not stopped.");

        f = Setup();
        f.OnFight = () => f.SetText(Fixture.ResultName, "Different opponent");
        Run(f.Task.Execute());
        Check(f.FightClicks == 1 && f.Result.activeSelf &&
              Logger.Messages.Any(s => s.Contains("result was unreadable or belonged to a different opponent")),
            "A different result was accepted.");

        f = Setup();
        f.CloseResult = false;
        Run(f.Task.Execute());
        Check(f.FightClicks == 1 && f.Result.activeSelf &&
              Logger.Messages.Any(s => s.Contains("result could not be closed")),
            "A result that could not be closed was accepted for another fight.");

        f = Setup();
        f.Won = false;
        Run(f.Task.Execute());
        Check(Logger.Messages.Any(s => s.Contains("completed: lost")), "The visible loss was not reported.");
    }

    public static void SquadRestoration()
    {
        var f = Setup();
        f.SetTokens(1);
        f.IntSetting("arena_squad").Value = 2;
        Run(f.Task.Execute());
        Check(f.FightClicks == 1 && SquadApplier.Requests.SequenceEqual(new[] { 2, 1 }) && SquadApplier.Active == 1,
            "The selected squad was not switched and restored around the fight.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        f.OpenPreview = false;
        Run(f.Task.Execute());
        Check(f.FightClicks == 0 && SquadApplier.Requests.SequenceEqual(new[] { 2, 1 }) && SquadApplier.Active == 1,
            "The squad was not restored after a preview failure.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        SquadApplier.OnSet = slot => SquadApplier.Active = slot == 2 ? 3 : slot;
        Run(f.Task.Execute());
        Check(f.FightClicks == 0 && SquadApplier.Requests.SequenceEqual(new[] { 2, 1 }) && SquadApplier.Active == 1,
            "A failed squad switch was not stopped and restored.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        SquadApplier.Active = 0;
        Run(f.Task.Execute());
        Check(SquadApplier.Requests.Count == 0 && f.PreviewClicks == 0,
            "An unknown active squad was allowed to fight.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        SquadApplier.Active = 4;
        Run(f.Task.Execute());
        Check(SquadApplier.Requests.Count == 0 && f.PreviewClicks == 0,
            "A squad that could not be restored was allowed to switch.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        SquadApplier.Populated.Remove(2);
        Run(f.Task.Execute());
        Check(SquadApplier.Requests.Count == 0 && f.PreviewClicks == 0, "An empty squad was allowed to fight.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        f.SetTokens(0);
        Run(f.Task.Execute());
        Check(SquadApplier.Requests.Count == 0 && f.PreviewClicks == 0, "A squad was switched without tokens.");

        f = Setup();
        f.SetTokens(1);
        f.IntSetting("arena_squad").Value = 2;
        SquadApplier.OnSet = slot => { if (slot == 2) SquadApplier.Active = 2; };
        Run(f.Task.Execute());
        Check(SquadApplier.Active == 2 && Logger.Messages.Any(s => s.Contains("Could not verify that squad 1 was restored")),
            "A failed squad restoration was not reported.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        SquadApplier.OnSet = slot =>
        {
            SquadApplier.Active = slot;
            if (slot == 2) f.Elapsed = 100;
        };
        Run(f.Task.Execute());
        Check(f.FightClicks == 0 && SquadApplier.Active == 1 &&
              SquadApplier.Requests.SequenceEqual(new[] { 2, 1 }),
            "The bot started a fight without enough time to restore the squad.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        Ui.BeforeGuardedClick = path =>
        {
            if (path == Fixture.PreviewFight) f.Elapsed = 75;
        };
        Run(f.Task.Execute());
        Check(f.FightClicks == 0 && SquadApplier.Active == 1,
            "The time budget was not rechecked before the fight click.");

        f = Setup();
        f.IntSetting("arena_squad").Value = 2;
        f.OnFight = () => f.Elapsed = 70;
        Run(f.Task.Execute());
        Check(f.FightClicks == 1 && SquadApplier.Active == 1,
            "Another fight was started when only the restore allowance remained.");
    }

    private static void SetAboveLimit(Fixture f)
    {
        f.SetOpponent(0, "Higher", 100, "1,300");
        f.SetOpponent(1, "Lowest", 101, "1,100");
        f.SetOpponent(2, "Highest", 102, "1,500");
    }

    private static Fixture Setup(DateTime? now = null)
    {
        GameObject.Clear();
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        Ui.BeforeGuardedClick = null;
        Logger.Messages.Clear();
        SquadApplier.Reset();
        return new Fixture(now ?? new DateTime(2026, 10, 30, 12, 0, 0));
    }

    private sealed class Fixture
    {
        public const string Menu = "menus/ArenaOfKings";
        public const string Arena = Menu + "/bg";
        public const string Formation = Arena + "/battleFormation/totalPower";
        public const string TokensPath = Arena + "/battleTokens/quantity";
        public const string Grid = Arena + "/opponentHub/opponentGrid";
        public const string PreviewPath = "popups/AOKBattlePreview";
        public const string PreviewFight = PreviewPath + "/bg/mask/fightButton";
        public const string Infos = PreviewPath + "/bg/mask/userInfos/";
        public const string Attacker = Infos + "arenaOfKingsAttackerInfoInteraction";
        public const string Defender = Infos + "arenaOfKingsDefenderInfoInteraction";
        public const string AttackerPower = Attacker + "/statsBg/powerBg/powerValue";
        public const string DefenderName = Defender + "/nameBg/nameText";
        public const string DefenderRank = Defender + "/statsBg/rankingBg/rankingValue";
        public const string DefenderPower = Defender + "/statsBg/powerBg/powerValue";
        public const string ResultPath = "popups/AOKBattleResult";
        public const string ResultName = ResultPath + "/bg/rankChanges/rightPlayer/nameBg/nameText";

        public readonly ArenaTask Task;
        public readonly MelonPreferences_Category Category;
        public readonly GameObject Preview;
        public readonly GameObject Result;
        public readonly Button FightButton;
        public readonly Button RefreshButton;
        public readonly GameObject WonObject;
        public readonly GameObject LostObject;
        public DateTime Now;
        public double Elapsed;
        public Action OnPreview;
        public Action OnFight;
        public Action OnRefresh;
        public bool OpenPreview = true;
        public bool Transition = true;
        public bool ShowResult = true;
        public bool CloseResult = true;
        public bool ThrowOnFight;
        public bool Won = true;
        public int PreviewClicks;
        public int FightClicks;
        public int RefreshClicks;
        public int TokenCount = 3;
        public string Selected;

        public Fixture(DateTime now)
        {
            Now = now;
            Task = new ArenaTask(() => Now, () => Elapsed);
            Task.InitializeConfig("test");
            Category = MelonPreferences.Categories["arena"];
            IntSetting("arena_squad").Value = 0;
            IntSetting("max_opponent_power_percent").Value = 100;
            BoolSetting("one_fight_daily").Value = true;
            StringSetting("last_forced_fight").Value = "";

            var menu = GameObject.CreatePath(Menu);
            GameObject.CreatePath("popups/WFMenuSelection/bg/arena").AddComponent<Button>().onClick.Action =
                () => menu.activeSelf = true;
            GameObject.CreatePath(Menu + "/closeButton").AddComponent<Button>().onClick.Action =
                () => menu.activeSelf = false;
            SetText(Formation, "Arena power: 1,000");
            SetTokens(TokenCount);
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                SetOpponent(i, "Opponent " + i, 100 + i, i == 0 ? "900" : i == 1 ? "800" : "1,200");
                GameObject.CreatePath(Card(i) + "/fightButton").AddComponent<Button>().onClick.Action =
                    () => Open(index);
            }
            RefreshButton = GameObject.CreatePath(Arena + "/opponentHub/refreshBg/refreshButton").AddComponent<Button>();
            RefreshButton.interactable = false;
            RefreshButton.onClick.Action = () => { RefreshClicks++; OnRefresh?.Invoke(); };

            Preview = GameObject.CreatePath(PreviewPath);
            SetText(AttackerPower, "1,000");
            SetText(DefenderName, "");
            SetText(DefenderRank, "");
            SetText(DefenderPower, "");
            GameObject.CreatePath(PreviewPath + "/bg/closeButton").AddComponent<Button>().onClick.Action =
                () => Preview.activeSelf = false;
            FightButton = GameObject.CreatePath(PreviewPath + "/bg/mask/fightButton").AddComponent<Button>();
            FightButton.onClick.Action = FinishFight;
            Preview.activeSelf = false;

            Result = GameObject.CreatePath(ResultPath);
            SetText(ResultName, "");
            WonObject = GameObject.CreatePath(ResultPath + "/bg/won");
            LostObject = GameObject.CreatePath(ResultPath + "/bg/lost");
            GameObject.CreatePath(ResultPath + "/bg/closeButton").AddComponent<Button>().onClick.Action =
                () => { if (CloseResult) Result.activeSelf = false; };
            Result.activeSelf = false;
            menu.activeSelf = false;
        }

        public string Card(int index) => Grid + "/card" + index;
        public MelonPreferences_Entry<int> IntSetting(string name) =>
            (MelonPreferences_Entry<int>)Category.Entries[name];
        public MelonPreferences_Entry<bool> BoolSetting(string name) =>
            (MelonPreferences_Entry<bool>)Category.Entries[name];
        public MelonPreferences_Entry<string> StringSetting(string name) =>
            (MelonPreferences_Entry<string>)Category.Entries[name];

        public void SetText(string path, string text)
        {
            GameObject.CreatePath(path);
            Ui.Texts[path] = text;
        }

        public void SetTokens(int count)
        {
            TokenCount = count;
            SetText(TokensPath, count + "/5");
        }

        public void SetOpponent(int index, string name, int rank, string power)
        {
            SetText(Card(index) + "/nameBg/nameText", name);
            SetText(Card(index) + "/rankingBg/rankingValue", rank.ToString());
            SetText(Card(index) + "/powerBg/powerValue", power);
        }

        private void Open(int index)
        {
            PreviewClicks++;
            Selected = Ui.Texts[Card(index) + "/nameBg/nameText"];
            if (!OpenPreview) return;
            SetText(DefenderName, Selected);
            SetText(DefenderRank, Ui.Texts[Card(index) + "/rankingBg/rankingValue"]);
            SetText(DefenderPower, Ui.Texts[Card(index) + "/powerBg/powerValue"]);
            Preview.activeSelf = true;
            OnPreview?.Invoke();
        }

        private void FinishFight()
        {
            FightClicks++;
            if (ThrowOnFight) throw new InvalidOperationException("Fight handler failed.");
            if (Transition) Preview.activeSelf = false;
            if (Transition && ShowResult)
            {
                SetTokens(TokenCount - 1);
                SetText(ResultName, Selected);
                WonObject.activeSelf = Won;
                LostObject.activeSelf = !Won;
                Result.activeSelf = true;
            }
            OnFight?.Invoke();
        }
    }
}
