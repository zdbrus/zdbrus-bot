using System;
using System.Collections;
using Firebot.Core;
using Il2Cpp;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

internal sealed class ScarabAutoPlay : IDisposable
{
    private const string S = P.Menus + "/ScarabGame";
    private const string H = S + "/helpCanvas";
    private const string Play = H + "/playButton";
    private const string Auto = H + "/bottomRightUI/autoPlayToggle";
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _allowPharaoh;
    private NativeAutoPlay _run;
    public bool ResumeSoon { get; private set; }

    public ScarabAutoPlay(Func<bool> enabled, Func<bool> allowPharaoh)
    {
        _enabled = enabled;
        _allowPharaoh = allowPharaoh;
    }

    public void Dispose()
    {
        _run?.Dispose();
        _run = null;
    }

    private static void StopAuto()
    {
        var mech = GameInitialize.MenuLoader?.ScarabGameMech;
        if (mech == null) return;
        mech.SetAutoPlayState(false);
        if (mech.autoPlayOn) throw new InvalidOperationException("Scarab Auto is still enabled.");
    }

    private ScarabSpinCurrency Currency() => ScarabSpinPolicy.SelectCurrency(
        Ui.Visible(Play + "/layout/nobleTokenIcon"), Ui.Visible(Play + "/layout/pharaohTokenIcon"), _allowPharaoh());

    private static string BalancePath(ScarabSpinCurrency currency) =>
        H + "/counters/currencyInteraction (" +
        (currency == ScarabSpinCurrency.Noble ? "NoblesToken" : "PharaohsToken") + ")/quantity";

    private static string CostPath(ScarabSpinCurrency currency) =>
        Play + (currency == ScarabSpinCurrency.Noble ? "/layout/costText1" : "/layout/costText2");

    public IEnumerator Execute()
    {
        var mech = GameInitialize.MenuLoader?.ScarabGameMech;
        if (!NativeAutoPlay.Ready || mech == null || !Ui.Visible(S))
        {
            Logger.Warning("[Scarab] Auto safety guards or live screen state are unavailable; no spin attempted.");
            yield break;
        }
        StopAuto();
        for (var step = 0; step < 8 && mech.currentPlayQuantity != 1; step++)
            yield return Ui.Click(H + "/bottomRightUI/changePlayQuantity", 0.2f);
        if (mech.currentPlayQuantity != 1)
        {
            Logger.Warning("[Scarab] x1 could not be selected; no spin attempted.");
            yield break;
        }
        var receipt = new AutoPlayReceipt();
        bool Affordable()
        {
            var currency = Currency();
            return currency != ScarabSpinCurrency.None && Ui.Visible(S) && mech.currentPlayQuantity == 1 &&
                   ScarabSpinPolicy.CanSpin(currency, Ui.NumberAt(BalancePath(currency)), Ui.NumberAt(CostPath(currency)));
        }
        if (!Affordable())
        {
            Logger.Info("[Scarab] No affordable permitted tokens. Pharaoh's tokens still require the separate opt-in.");
            yield break;
        }

        bool Permit(PlayTokenSelection selection)
        {
            var currency = Currency();
            var expected = currency == ScarabSpinCurrency.Noble ? CurrencyCode.NoblesToken : CurrencyCode.PharaohsToken;
            if (!_enabled() || !BotManager.IsRunning || !receipt.Observe() || !Affordable() ||
                selection.SelectedCurrency != expected || !selection.HasEnoughTokens ||
                selection.TokensNeeded != Ui.NumberAt(CostPath(currency)))
            {
                Logger.Info("[Scarab] Auto stopped at the token permission, balance, mode, or receipt boundary.");
                return false;
            }
            var path = BalancePath(currency);
            receipt.Expect(() => Ui.NumberAt(path), Ui.NumberAt(path), selection.TokensNeeded, 1);
            return true;
        }

        try
        {
            _run = new NativeAutoPlay(AutoPlayKind.Scarab, Permit, StopAuto);
            var toggled = false;
            yield return Ui.ClickIf(Auto, () => !mech.autoPlayOn && Affordable(),
                value => toggled = value, 0.3f, "Scarab Auto");
            if (!toggled || (!mech.autoPlayOn && !_run.Stopped))
            {
                Logger.Warning("[Scarab] Auto mode could not be confirmed; no further action taken.");
                yield break;
            }
            if (_run.Requests == 0 && !_run.Stopped)
                yield return Ui.ClickIf(Play, () => mech.autoPlayOn && Affordable() && !_run.Stopped,
                    _ => { }, 0.3f, "start Scarab Auto");

            var started = Time.unscaledTime;
            var lastProgress = started;
            var confirmed = 0;
            while (!_run.Stopped)
            {
                receipt.Observe();
                if (receipt.Confirmed != confirmed)
                {
                    confirmed = receipt.Confirmed;
                    lastProgress = Time.unscaledTime;
                }
                if (receipt.Invalid || !_enabled() || !Ui.Visible(S) || !mech.autoPlayOn ||
                    (!receipt.Pending && !Affordable())) break;
                if (Time.unscaledTime - lastProgress > 20f)
                {
                    Logger.Warning("[Scarab] Auto made no confirmed progress for 20 seconds; stopping.");
                    break;
                }
                if (Time.unscaledTime - started > Math.Max(1, BotSettings.MaxTaskRuntime - 15))
                {
                    Logger.Info("[Scarab] Reached the task time limit; leaving remaining tokens for a later visit.");
                    ResumeSoon = true;
                    break;
                }
                yield return new WaitForSeconds(0.1f);
            }
            _run.Stop();
            if (receipt.Pending && !receipt.Invalid)
                yield return Ui.WaitUntil(receipt.Observe, _ => { }, 8f);
            if (receipt.Pending) Logger.Warning("[Scarab] The final spin's token use remains unconfirmed.");
            // Let the last authorized roll finish before navigating to another task.
            yield return Ui.WaitUntil(() => !mech.isRolling && !mech.askedServerForRoll, _ => { }, 8f);
            Logger.Info($"[Scarab] Auto confirmed {receipt.Confirmed} spin(s).");
        }
        finally { Dispose(); }
    }
}
