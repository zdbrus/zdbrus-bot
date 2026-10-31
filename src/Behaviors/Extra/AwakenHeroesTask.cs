using System;
using System.Collections;
using Firebot.Core;
using Firebot.Core.Tasks;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

public class AwakenHeroesTask : BotTask
{
    protected override string NotificationPath => P.Notifications + "/Awakening";
    protected override bool UseScheduledTime => true;
    private const string A = P.Menus + "/Awakening";
    private const string Button = A + "/awakeningOptions/awakenBg/awakenButton";
    private const string Crystals = A + "/counters/currencyInteraction (ArcaneCrystal)/quantity";
    private const string Auto = A + "/autoAwakenToggle";
    private MelonPreferences_Entry<int> _quantity;
    private NativeAutoPlay _autoRun;

    protected override void OnConfigure(MelonPreferences_Category category)
    {
        _quantity = category.CreateEntry("quantity", 1, "Awaken Amount",
            "Auto awakens in x1, x2, x5, x10 or x20 batches until another batch is unaffordable. Uses Arcane Crystals, never gems.");
    }

    internal static bool ValidQuantity(int quantity) => quantity is 1 or 2 or 5 or 10 or 20;

    internal override void OnStopped()
    {
        _autoRun?.Dispose();
        _autoRun = null;
    }

    private static void StopAuto()
    {
        var mech = GameInitialize.MenuLoader?.AwakeningMech;
        if (mech == null) return;
        if (mech.autoAwakenOn) mech.AutoAwakenToggle();
        if (mech.autoAwakenOn) throw new InvalidOperationException("Awakening Auto is still enabled.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddHours(3);
        BubbleQuietUntil = DateTime.Now.AddHours(1);
        var quantity = _quantity.Value;
        if (!ValidQuantity(quantity) || !NativeAutoPlay.Ready)
        {
            Logger.Warning("[Awakening] Invalid amount or unavailable Auto safety guards; nothing spent.");
            yield break;
        }
        StopAuto();
        yield return Ui.OpenGuildBuilding("awakening");
        var mech = GameInitialize.MenuLoader?.AwakeningMech;
        if (!Ui.Visible(A) || mech == null)
        {
            Logger.Warning("[Awakening] The screen or its live state is unavailable; nothing spent.");
            yield break;
        }

        var selector = A + "/awakeningOptions/" +
                       (quantity == 20 ? "awakeningQuantityGrid1" : "awakeningQuantityGrid") +
                       "/changeQuantityButton" + quantity;
        if ((int)mech.awakeningQuantity != quantity)
            yield return Ui.Click(selector, 0.3f);
        if ((int)mech.awakeningQuantity != quantity)
        {
            Logger.Warning($"[Awakening] x{quantity} is locked or could not be selected; no fallback amount used.");
            yield break;
        }

        var receipt = new AutoPlayReceipt();
        bool Affordable() => Ui.Visible(A) && Ui.Visible(Button + "/arcaneCrystalIcon") &&
                             (int)mech.awakeningQuantity == quantity &&
                             AutoPlayReceipt.Affordable(Ui.NumberAt(Crystals), Ui.NumberAt(Button + "/costText"));
        if (!Affordable())
        {
            Logger.Info($"[Awakening] No affordable x{quantity} batch; crystals {Ui.NumberAt(Crystals)}, cost {Ui.NumberAt(Button + "/costText")}.");
            yield return Ui.Click(A + "/closeButton", 0.5f);
            yield break;
        }

        bool Permit(PlayTokenSelection _)
        {
            if (!IsEnabled || !BotManager.IsRunning || _quantity.Value != quantity || !receipt.Observe() || !Affordable())
            {
                Logger.Info("[Awakening] Auto stopped: no next affordable selected batch, changed settings, or unconfirmed spending.");
                return false;
            }
            receipt.Expect(() => Ui.NumberAt(Crystals), Ui.NumberAt(Crystals),
                Ui.NumberAt(Button + "/costText"), quantity);
            return true;
        }

        try
        {
            _autoRun = new NativeAutoPlay(AutoPlayKind.Awakening, Permit, StopAuto);
            var toggled = false;
            yield return Ui.ClickIf(Auto, () => !mech.autoAwakenOn && Affordable(),
                value => toggled = value, 0.3f, $"Auto awakening x{quantity}");
            if (!toggled || (!mech.autoAwakenOn && !_autoRun.Stopped))
            {
                Logger.Warning("[Awakening] Auto mode could not be confirmed; nothing else attempted.");
                yield break;
            }
            // Some game versions start on the toggle; others need one initial Awaken press.
            if (_autoRun.Requests == 0 && !_autoRun.Stopped)
                yield return Ui.ClickIf(Button, () => mech.autoAwakenOn && Affordable() && !_autoRun.Stopped,
                    _ => { }, 0.3f, "start Auto awakening");

            var started = Time.unscaledTime;
            var lastProgress = started;
            var confirmed = 0;
            while (!_autoRun.Stopped)
            {
                receipt.Observe();
                if (receipt.Confirmed != confirmed)
                {
                    confirmed = receipt.Confirmed;
                    lastProgress = Time.unscaledTime;
                }
                if (receipt.Invalid || !IsEnabled || !Ui.Visible(A) || !mech.autoAwakenOn ||
                    (!receipt.Pending && !Affordable())) break;
                if (Time.unscaledTime - lastProgress > 15f)
                {
                    Logger.Warning("[Awakening] Auto made no confirmed progress for 15 seconds; stopping.");
                    break;
                }
                if (Time.unscaledTime - started > Math.Max(1, BotSettings.MaxTaskRuntime - 10))
                {
                    Logger.Info("[Awakening] Reached the task time limit; remaining batches will resume later.");
                    NextRunTime = DateTime.Now.AddMinutes(1);
                    BubbleQuietUntil = NextRunTime;
                    break;
                }
                yield return new WaitForSeconds(0.1f);
            }
            _autoRun.Stop();
            if (receipt.Pending && !receipt.Invalid)
                yield return Ui.WaitUntil(receipt.Observe, _ => { }, 8f);
            if (receipt.Pending) Logger.Warning("[Awakening] The final batch's crystal use remains unconfirmed.");
            Logger.Info($"[Awakening] Auto confirmed {receipt.Confirmed} awakening(s) at x{quantity}; crystals now {Ui.NumberAt(Crystals)}.");
        }
        finally { OnStopped(); }

        if (Ui.Visible(A)) yield return Ui.Click(A + "/closeButton", 0.5f);
    }
}
