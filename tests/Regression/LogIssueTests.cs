using System.Collections;
using Firebot.Behaviors.Extra;
using Firebot.Behaviors.Guild;
using Firebot.Behaviors.Town;
using Firebot.Core;
using Firebot.GameModel.Primitives;
using Firebot.GameModel.Shared;
using Firebot.Utilities;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

internal static class LogIssueTests
{
    private const string PickaxeRoot = "menus/GuildShop/bg/submenus/supplies/items/freePickaxe";
    private const string PickaxeQuantity = PickaxeRoot + "/claimBg/itemBg/itemQuantity";
    private const string PickaxeClaim = PickaxeRoot + "/claimBg/purchaseButton";
    private const string PickaxeMarker = PickaxeRoot + "/claimBg/claimedBG";

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Run(IEnumerator routine)
    {
        var steps = 0;
        while (routine.MoveNext())
        {
            if (++steps > 2000) throw new InvalidOperationException("Coroutine did not finish.");
            if (routine.Current is IEnumerator child) Run(child);
            Time.unscaledTime += 0.1f;
        }
    }

    private static (FreePickaxesTask Task, Button Claim, GameObject Marker) PickaxeFixture(string quantity)
    {
        GameObject.Clear();
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        GameText.Texts.Clear();
        GameText.Times.Clear();
        Logger.Messages.Clear();
        var task = new FreePickaxesTask();
        task.InitializeConfig("test");
        task.SetEnabledFromStatus(true);
        ((MelonPreferences_Entry<int>)MelonPreferences.Categories["free_pickaxes"]
            .Entries["pickaxe_claim_threshold"]).Value = 30;
        GameObject.CreatePath("notifications/FreePickaxes");
        var claim = GameObject.CreatePath(PickaxeClaim).AddComponent<Button>();
        var marker = GameObject.CreatePath(PickaxeMarker);
        marker.activeSelf = false;
        GameObject.CreatePath("menus/GuildShop/closeButton").AddComponent<Button>();
        GameText.Texts[PickaxeQuantity] = quantity;
        return (task, claim, marker);
    }

    public static void FreePickaxeClaims()
    {
        Check(StringUtils.TryParseCount(" x1,000 ", out var count) && count == 1000 &&
              !StringUtils.TryParseCount("x1,00", out _) &&
              !StringUtils.TryParseCount("x-1", out _) &&
              !StringUtils.TryParseCount("x1.5", out _),
            "Item counts accepted malformed text or rejected a valid grouped count.");

        var f = PickaxeFixture("x30");
        f.Claim.onClick.Action = () => GameText.Texts[PickaxeQuantity] = "x0";
        Run(f.Task.Execute());
        Check(Logger.Messages.Any(s => s.Contains("claim was confirmed")),
            "A readable decrease in unclaimed pickaxes was not confirmed.");

        f = PickaxeFixture("x30");
        f.Claim.onClick.Action = () =>
        {
            GameText.Texts[PickaxeQuantity] = "";
            f.Marker.activeSelf = true;
        };
        Run(f.Task.Execute());
        Check(Logger.Messages.Any(s => s.Contains("claim was confirmed")),
            "A newly visible claimed marker was not confirmed.");

        f = PickaxeFixture("x30");
        f.Claim.onClick.Action = () =>
        {
            GameText.Texts[PickaxeQuantity] = "";
            f.Claim.interactable = false;
        };
        Check(f.Task.IsNotificationVisible(), "The pickaxe notification fixture is not visible.");
        var started = DateTime.Now;
        Run(f.Task.Execute());
        Check(!Logger.Messages.Any(s => s.Contains("claim was confirmed")) &&
              Logger.Messages.Any(s => s.Contains("could not be confirmed")) &&
              f.Task.NextRunTime >= started.AddMinutes(9.9) &&
              f.Task.BubbleQuietUntil >= f.Task.NextRunTime &&
              !f.Task.IsNotificationVisible() && !f.Task.IsReady(),
            "An unconfirmed pickaxe claim was reported as successful or retriggered before its retry.");
        ScheduleStore.Save(new[] { f.Task });
        var restartedPickaxes = new FreePickaxesTask();
        restartedPickaxes.InitializeConfig("test");
        restartedPickaxes.SetEnabledFromStatus(true);
        ScheduleStore.Restore(new[] { restartedPickaxes });
        Check(restartedPickaxes.NextRunTime == f.Task.NextRunTime &&
              restartedPickaxes.BubbleQuietUntil >= f.Task.NextRunTime &&
              !restartedPickaxes.IsNotificationVisible() && !restartedPickaxes.IsReady(),
            "Restarting allowed a pickaxe notification to bypass its retry delay.");
        restartedPickaxes.RestoreNextRun(DateTime.Now.AddSeconds(-1));
        Check(restartedPickaxes.IsReady() && !restartedPickaxes.IsNotificationVisible(),
            "A restored notification cooldown blocked the scheduled retry.");
        f.Task.RestoreNextRun(DateTime.Now.AddSeconds(-1));
        Check(f.Task.IsReady() && !f.Task.IsNotificationVisible(),
            "The scheduled pickaxe retry was blocked by the quiet notification.");

        f = PickaxeFixture("x30");
        f.Claim.onClick.Action = () => GameText.Texts[PickaxeQuantity] = "x-1";
        Run(f.Task.Execute());
        Check(!Logger.Messages.Any(s => s.Contains("claim was confirmed")) &&
              Logger.Messages.Any(s => s.Contains("could not be confirmed")),
            "A malformed negative quantity falsely confirmed a pickaxe claim.");

        f = PickaxeFixture("");
        var clicks = 0;
        f.Claim.onClick.Action = () => clicks++;
        started = DateTime.Now;
        Run(f.Task.Execute());
        Check(clicks == 0 && Logger.Messages.Any(s => s.Contains("quantity could not be read")) &&
              f.Task.NextRunTime >= started.AddMinutes(9.9) &&
              f.Task.BubbleQuietUntil >= f.Task.NextRunTime &&
              !f.Task.IsNotificationVisible() && !f.Task.IsReady(),
            "An unreadable starting quantity was clicked or retriggered before its retry.");

        f = PickaxeFixture("x1.5");
        clicks = 0;
        f.Claim.onClick.Action = () => clicks++;
        Run(f.Task.Execute());
        Check(clicks == 0 && Logger.Messages.Any(s => s.Contains("quantity could not be read")),
            "A malformed starting quantity was accepted as an integer.");

        f = PickaxeFixture("x10");
        clicks = 0;
        f.Claim.onClick.Action = () => clicks++;
        Run(f.Task.Execute());
        Check(clicks == 0 && Logger.Messages.Any(s => s.Contains("10 available")),
            "A quantity below the configured threshold was claimed.");
    }

    public static void ChaosRiftCooldown()
    {
        GameObject.Clear();
        Ui.Texts.Clear();
        Ui.Numbers.Clear();
        Ui.BeforeGuardedClick = null;
        Logger.Messages.Clear();
        var now = DateTime.Now;
        var task = new ChaosRiftTask(() => now);
        task.InitializeConfig("test");
        task.SetEnabledFromStatus(true);
        var settings = MelonPreferences.Categories["chaos_rift"].Entries;
        ((MelonPreferences_Entry<string>)settings["holy_guardians"]).Value = "";

        var message = GameObject.CreatePath("popups/GenericMessage");
        message.activeSelf = false;
        Ui.Texts["popups/GenericMessage/bg/messageText"] = "The rift is closed.";
        var riftClicks = 0;
        GameObject.CreatePath("notifications/ChaosRift").AddComponent<Button>().onClick.Action = () =>
        {
            riftClicks++;
            message.activeSelf = true;
        };
        GameObject.CreatePath("popups/GenericMessage/bg/closeButton").AddComponent<Button>().onClick.Action =
            () => message.activeSelf = false;
        Run(task.Execute());
        Check(riftClicks == 1 && !message.activeSelf && task.NextRunTime == now.AddHours(6) &&
              Logger.Messages.Any(s => s.Contains("The rift is closed.")),
            "The closed-rift message was not logged, closed, and deferred for six hours.");
        Check(!task.IsNotificationVisible(), "A blocked Rift notification bypassed its cooldown.");
        ScheduleStore.Save(new[] { task });
        task = new ChaosRiftTask(() => now);
        task.InitializeConfig("test");
        task.SetEnabledFromStatus(true);
        settings = MelonPreferences.Categories["chaos_rift"].Entries;
        ((MelonPreferences_Entry<string>)settings["holy_guardians"]).Value = "";
        ScheduleStore.Restore(new[] { task });
        Check(task.NextRunTime == now.AddHours(6) && !task.IsNotificationVisible(),
            "Restarting allowed a Rift notification to bypass its cooldown.");

        var guardian = GameObject.CreatePath("notifications/GuardianHolyUpgrade");
        guardian.activeSelf = false;
        var quarters = GameObject.CreatePath("menus/MagicQuarters");
        quarters.activeSelf = false;
        var guardianClicks = 0;
        guardian.AddComponent<Button>().onClick.Action = () =>
        {
            guardianClicks++;
            quarters.activeSelf = true;
        };
        GameObject.CreatePath("menus/MagicQuarters/closeButton").AddComponent<Button>().onClick.Action =
            () => quarters.activeSelf = false;
        guardian.activeSelf = true;
        Check(task.IsNotificationVisible(), "A guardian upgrade was muted by the Rift cooldown.");
        Run(task.Execute());
        Check(guardianClicks == 1 && riftClicks == 1 && !quarters.activeSelf &&
              task.NextRunTime == now.AddHours(6),
            "A guardian notification retried the Rift or failed to open and close Magic Quarters.");

        ((MelonPreferences_Entry<bool>)settings["holy_damage_upgrades"]).Value = false;
        Check(!task.IsNotificationVisible(), "Disabled holy upgrades triggered during the Rift cooldown.");
        guardian.activeSelf = false;
        now = now.AddHours(6);
        Check(task.IsNotificationVisible(), "The Rift notification remained muted after its cooldown.");
        Run(task.Execute());
        Check(riftClicks == 2, "The Rift was not retried when its cooldown expired.");
    }

    public static void MysteryBoxClaims()
    {
        const string storePath = "menus/Store";
        const string tabPath = storePath + "/submenuButtons/grid/valueBundleButton";
        const string item = storePath + "/bg/submenus/extremeValueBundles/bundles/mysteryBox/Graphics";
        const string checkIn = storePath + "/bg/submenus/dailyRewards/Viewport/transparentFrame/checkIn";

        (DailyRewardsTask Task, GameObject Store, Button Tab, Button Claim, GameObject Marker) Setup(
            Func<DateTime> clock = null)
        {
            GameObject.Clear();
            Ui.Texts.Clear();
            Ui.Numbers.Clear();
            Ui.BeforeGuardedClick = null;
            Logger.Messages.Clear();
            Il2Cpp.GameInitialize.Loaded = true;
            Il2Cpp.GameInitialize.HandlerLoader = new();
            Il2Cpp.GameInitialize.HandlerLoader.CharacterLevel.Level = 50;
            Notifications.OnMysteryBox = null;
            Notifications.OnCheckIn = null;
            var now = clock?.Invoke() ?? DateTime.Now;
            Store.MysteryBoxNextRunTime = now.AddHours(24);
            Store.CheckInNextRunTime = now.AddHours(24);
            var task = clock == null ? new DailyRewardsTask() : new DailyRewardsTask(clock);
            task.InitializeConfig("test");
            task.SetEnabledFromStatus(true);
            GameObject.CreatePath("notifications/MysteryBox");
            var store = GameObject.CreatePath(storePath);
            store.activeSelf = false;
            Notifications.OnMysteryBox = () => store.activeSelf = true;
            var tab = GameObject.CreatePath(tabPath).AddComponent<Button>();
            var claim = GameObject.CreatePath(item + "/purchaseButton").AddComponent<Button>();
            claim.gameObject.activeSelf = false;
            var marker = GameObject.CreatePath(item + "/claimedBG");
            marker.activeSelf = false;
            GameObject.CreatePath(storePath + "/closeButton").AddComponent<Button>().onClick.Action =
                () => store.activeSelf = false;
            return (task, store, tab, claim, marker);
        }

        var f = Setup();
        var switches = 0;
        var claims = 0;
        f.Tab.onClick.Action = () => { switches++; f.Claim.gameObject.activeSelf = true; };
        f.Claim.onClick.Action = () => { claims++; f.Marker.activeSelf = true; };
        Run(f.Task.Execute());
        Check(switches == 1 && claims == 1 && !f.Store.activeSelf &&
              f.Task.NextRunTime > DateTime.Now.AddHours(23) && !f.Task.IsNotificationVisible(),
            "The Mystery Box tab was not switched, claimed, and temporarily quieted.");
        ScheduleStore.Save(new[] { f.Task });
        var restartedRewards = new DailyRewardsTask();
        restartedRewards.InitializeConfig("test");
        restartedRewards.SetEnabledFromStatus(true);
        ScheduleStore.Restore(new[] { restartedRewards });
        Check(!restartedRewards.IsNotificationVisible(),
            "Restarting allowed a claimed Mystery Box notification to bypass its quiet period.");

        var checkInBubble = GameObject.CreatePath("notifications/CheckIn");
        var checkInClicks = 0;
        GameObject.CreatePath(checkIn).AddComponent<Button>().onClick.Action = () => checkInClicks++;
        Notifications.OnCheckIn = () => f.Store.activeSelf = true;
        Check(restartedRewards.IsNotificationVisible(), "Mystery Box backoff muted a separate check-in notification.");
        Run(restartedRewards.Execute());
        Check(switches == 1 && claims == 1 && checkInClicks == 1,
            "A check-in notification retried the Mystery Box during its backoff.");
        checkInBubble.activeSelf = false;

        f = Setup();
        claims = 0;
        f.Tab.onClick.Action = () => f.Marker.activeSelf = true;
        f.Claim.onClick.Action = () => claims++;
        Run(f.Task.Execute());
        Check(claims == 0 && Logger.Messages.Any(s => s.Contains("already claimed")) &&
              !Logger.Messages.Any(s => s.Contains("could not be found")),
            "An already-claimed Mystery Box was clicked or reported as unavailable.");

        f = Setup();
        var started = DateTime.Now;
        Run(f.Task.Execute());
        Check(Logger.Messages.Any(s => s.Contains("could not be found or claimed")) &&
              f.Task.NextRunTime >= started.AddMinutes(59.9) &&
              f.Task.NextRunTime <= started.AddMinutes(60.1) && !f.Task.IsNotificationVisible(),
            "An unreadable Mystery Box was reported as unavailable or not scheduled for retry.");

        f = Setup();
        f.Tab.onClick.Action = () => f.Claim.gameObject.activeSelf = true;
        f.Claim.onClick.Action = () => f.Store.activeSelf = false;
        started = DateTime.Now;
        Run(f.Task.Execute());
        Check(f.Task.NextRunTime >= started.AddMinutes(59.9) &&
              f.Task.NextRunTime <= started.AddMinutes(60.1),
            "A disappearing store was treated as a confirmed Mystery Box claim.");

        f = Setup();
        f.Tab.onClick.Action = () => f.Claim.gameObject.activeSelf = true;
        f.Claim.onClick.Action = () => f.Claim.interactable = false;
        started = DateTime.Now;
        Run(f.Task.Execute());
        Check(f.Task.NextRunTime >= started.AddMinutes(59.9) &&
              f.Task.NextRunTime <= started.AddMinutes(60.1),
            "A disabled button without a claimed marker or rewards was treated as a successful claim.");

        f = Setup();
        claims = 0;
        f.Tab.onClick.Action = () => f.Claim.gameObject.activeSelf = true;
        f.Claim.onClick.Action = () => { claims++; f.Marker.activeSelf = true; };
        GameObject.CreatePath(item + "/purchaseButton/priceHolder/freeText");
        Run(f.Task.Execute());
        Check(claims == 1, "A standard free-price holder prevented a Mystery Box claim.");

        f = Setup();
        claims = 0;
        f.Tab.onClick.Action = () => f.Claim.gameObject.activeSelf = true;
        f.Claim.onClick.Action = () => claims++;
        GameObject.CreatePath(item + "/purchaseButton/priceHolder/freeText");
        GameObject.CreatePath(item + "/purchaseButton/priceHolder/currencyIcon");
        Run(f.Task.Execute());
        Check(claims == 0 && f.Task.NextRunTime > DateTime.Now.AddMinutes(59),
            "A Mystery Box store offer displaying a price was clicked.");

        f = Setup();
        claims = 0;
        f.Tab.onClick.Action = () => f.Claim.gameObject.activeSelf = true;
        f.Claim.onClick.Action = () => claims++;
        Ui.BeforeGuardedClick = path =>
        {
            if (path == item + "/purchaseButton")
                GameObject.CreatePath(item + "/purchaseButton/gemCost");
        };
        Run(f.Task.Execute());
        Check(claims == 0 && f.Task.NextRunTime > DateTime.Now.AddMinutes(59),
            "A Mystery Box store offer that became priced before the click was used.");
        Ui.BeforeGuardedClick = null;

        var now = DateTime.Now;
        f = Setup(() => now);
        GameObject.Resolve("notifications/MysteryBox").activeSelf = false;
        GameObject.CreatePath(checkIn).AddComponent<Button>();
        Notifications.OnCheckIn = () => f.Store.activeSelf = true;
        var tabClicks = 0;
        f.Tab.onClick.Action = () => tabClicks++;
        Run(f.Task.Execute());
        var retryAt = now.AddHours(1);
        Check(tabClicks == 1 && f.Task.NextRunTime == retryAt,
            "A later daily reward overwrote the scheduled Mystery Box retry.");

        ScheduleStore.Save(new[] { f.Task });
        restartedRewards = new DailyRewardsTask(() => now);
        restartedRewards.InitializeConfig("test");
        restartedRewards.SetEnabledFromStatus(true);
        ScheduleStore.Restore(new[] { restartedRewards });
        GameObject.CreatePath("notifications/CheckIn");
        now = now.AddMinutes(30);
        Run(restartedRewards.Execute());
        Check(tabClicks == 1 && restartedRewards.NextRunTime == retryAt,
            "An unrelated notification postponed the pending Mystery Box retry.");

        now = retryAt;
        Run(restartedRewards.Execute());
        Check(tabClicks == 2 && restartedRewards.NextRunTime == now.AddHours(1),
            "A due Mystery Box retry was skipped while another reward notification was visible.");

        now = DateTime.Now.AddHours(-2);
        f = Setup(() => now);
        GameObject.Resolve("notifications/MysteryBox").activeSelf = false;
        GameObject.CreatePath(checkIn).AddComponent<Button>();
        Notifications.OnCheckIn = () => f.Store.activeSelf = true;
        tabClicks = 0;
        f.Tab.onClick.Action = () => tabClicks++;
        Run(f.Task.Execute());
        ScheduleStore.Save(new[] { f.Task });
        restartedRewards = new DailyRewardsTask(() => now);
        restartedRewards.InitializeConfig("test");
        restartedRewards.SetEnabledFromStatus(true);
        ScheduleStore.Restore(new[] { restartedRewards });
        now = DateTime.Now;
        GameObject.CreatePath("notifications/CheckIn");
        Run(restartedRewards.Execute());
        Check(tabClicks == 2,
            "An expired pending Mystery Box retry was forgotten after a restart.");
        Notifications.OnMysteryBox = null;
        Notifications.OnCheckIn = null;
    }

    public static void MysteryBoxOpening()
    {
        const string inventory = "menus/Inventory";
        const string content = inventory + "/submenus/items/ScrollView/Viewport/Content";
        const string boxPath = content + "/mysteryBox";
        const string popupPath = "popups/MysteryBoxOpenPreview";
        const string preview = popupPath + "/bg";
        const string max = preview + "/openingOptions/openxMax";

        (GameObject Inventory, GameObject Box, GameObject Popup, Button Tile, Button Open) Setup()
        {
            GameObject.Clear();
            Ui.Texts.Clear();
            Ui.Numbers.Clear();
            Ui.MissingCurrency = false;
            Ui.OnPointerClick = null;
            Ui.BeforeGuardedClick = null;
            Logger.Messages.Clear();
            Time.unscaledTime = 0;
            var bag = GameObject.CreatePath(inventory);
            var box = GameObject.CreatePath(boxPath);
            var tile = box.AddComponent<Button>();
            GameObject.CreatePath(boxPath + "/quantity");
            Ui.Texts[boxPath + "/quantity"] = "6";
            var popup = GameObject.CreatePath(popupPath);
            popup.activeSelf = false;
            tile.onClick.Action = () => popup.activeSelf = true;
            GameObject.CreatePath(preview + "/closeButton").AddComponent<Button>().onClick.Action =
                () => popup.activeSelf = false;
            var open = GameObject.CreatePath(max).AddComponent<Button>();
            GameObject.CreatePath(max + "/chestIcon");
            GameObject.CreatePath(max + "/quantity");
            Ui.Texts[max + "/quantity"] = "x6";
            return (bag, box, popup, tile, open);
        }

        var f = Setup();
        var clicks = 0;
        f.Open.onClick.Action = () => { clicks++; Ui.Texts[boxPath + "/quantity"] = "0"; };
        Run(MysteryBoxOpener.Open(content));
        Check(clicks == 1 && !f.Popup.activeSelf &&
              Logger.Messages.Any(s => s.Contains("Opened 6 Mystery Box")),
            "A free Mystery Box opening with a readable count decrease was not confirmed.");

        f = Setup();
        f.Open.onClick.Action = () => Ui.Texts[boxPath + "/quantity"] = "";
        Run(MysteryBoxOpener.Open(content));
        Check(!Logger.Messages.Any(s => s.Contains("Opened")) &&
              Logger.Messages.Any(s => s.Contains("could not be confirmed")),
            "An unreadable remaining count confirmed a Mystery Box opening.");

        f = Setup();
        f.Open.onClick.Action = () => Ui.Texts[boxPath + "/quantity"] = "x-1";
        Run(MysteryBoxOpener.Open(content));
        Check(!Logger.Messages.Any(s => s.Contains("Opened")) &&
              Logger.Messages.Any(s => s.Contains("could not be confirmed")),
            "A malformed negative count confirmed a Mystery Box opening.");

        f = Setup();
        f.Open.onClick.Action = () => f.Inventory.activeSelf = false;
        Run(MysteryBoxOpener.Open(content));
        Check(!Logger.Messages.Any(s => s.Contains("Opened")) &&
              Logger.Messages.Any(s => s.Contains("could not be confirmed")),
            "A hidden inventory confirmed a Mystery Box opening.");

        f = Setup();
        clicks = 0;
        f.Open.onClick.Action = () => clicks++;
        GameObject.CreatePath(max + "/gemCost");
        Run(MysteryBoxOpener.Open(content));
        Check(clicks == 0 && !f.Popup.activeSelf && Logger.Messages.Any(s => s.Contains("no safe free")),
            "A Mystery Box button showing a gem cost was used.");

        f = Setup();
        clicks = 0;
        f.Open.onClick.Action = () => clicks++;
        Ui.BeforeGuardedClick = path =>
        {
            if (path == max) GameObject.CreatePath(max + "/gemCost");
        };
        Run(MysteryBoxOpener.Open(content));
        Check(clicks == 0 && !f.Popup.activeSelf &&
              Logger.Messages.Any(s => s.Contains("state changed before opening")),
            "A Mystery Box opening that became priced before the click was used.");
        Ui.BeforeGuardedClick = null;

        f = Setup();
        clicks = 0;
        f.Open.onClick.Action = () => clicks++;
        GameObject.CreatePath(preview + "/chestRequirementBg");
        Run(MysteryBoxOpener.Open(content));
        Check(clicks == 0 && Logger.Messages.Any(s => s.Contains("opening requirement")),
            "A Mystery Box with an opening requirement was used.");

        f = Setup();
        clicks = 0;
        f.Open.onClick.Action = () => clicks++;
        Ui.Texts[max + "/quantity"] = "x7";
        Run(MysteryBoxOpener.Open(content));
        Check(clicks == 0 && Logger.Messages.Any(s => s.Contains("exceeds the owned count")),
            "An opening amount larger than the owned count was used.");

        f = Setup();
        clicks = 0;
        f.Open.onClick.Action = () => clicks++;
        Ui.Texts[max + "/quantity"] = "x1.5";
        Run(MysteryBoxOpener.Open(content));
        Check(clicks == 0 && Logger.Messages.Any(s => s.Contains("opening amount is unreadable")),
            "A malformed opening amount was accepted as an integer.");

        f = Setup();
        f.Open.onClick.Action = () => f.Box.activeSelf = false;
        Run(MysteryBoxOpener.Open(content));
        Check(Logger.Messages.Any(s => s.Contains("Opened 6 Mystery Box")),
            "The disappearance of the final box from a visible inventory was not confirmed.");

        f = Setup();
        f.Tile.onClick.Action = () => { };
        Ui.OnPointerClick = _ => f.Popup.activeSelf = true;
        f.Open.onClick.Action = () => Ui.Texts[boxPath + "/quantity"] = "0";
        Run(MysteryBoxOpener.Open(content));
        Check(Logger.Messages.Any(s => s.Contains("Opened 6 Mystery Box")),
            "A Mystery Box tile requiring a pointer click was not opened.");
        Ui.OnPointerClick = null;
    }
}
