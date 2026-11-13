using System;
using Firebot.Core;
using HarmonyLib;
using Il2Cpp;

namespace Firebot.Behaviors.Extra;

internal enum AutoPlayKind { Awakening, Scarab }

/// <summary>Guards every native Auto request, including requests made between bot coroutine steps.</summary>
internal sealed class NativeAutoPlay : IDisposable
{
    private static NativeAutoPlay _current;
    internal static bool Ready { get; private set; }
    private readonly AutoPlayKind _kind;
    private readonly Func<PlayTokenSelection, bool> _allowRequest;
    private readonly Action _stopNative;
    private bool _stoppedCleanly;
    private bool _expectScarabSequence;
    private PlayTokenSelection _scarabSelection;
    public bool Stopped { get; private set; }
    public int Requests { get; private set; }

    public static void Initialize(HarmonyLib.Harmony harmony)
    {
        try
        {
            harmony.Patch(AccessTools.Method(typeof(AwakeningMech), nameof(AwakeningMech.AwakenHeroAskServer)),
                prefix: new HarmonyMethod(typeof(NativeAutoPlay), nameof(AwakeningPrefix)));
            harmony.Patch(AccessTools.Method(typeof(ScarabGameMech), nameof(ScarabGameMech.BeginRollSequence)),
                prefix: new HarmonyMethod(typeof(NativeAutoPlay), nameof(ScarabPrefix)));
            harmony.Patch(AccessTools.Method(typeof(ScarabGameMech), nameof(ScarabGameMech.StartRolling)),
                prefix: new HarmonyMethod(typeof(NativeAutoPlay), nameof(ScarabStartPrefix)));
            Ready = true;
        }
        catch (Exception e)
        {
            Ready = false;
            Logger.Error($"[Auto play] Safety guards could not be installed; automatic spending is disabled. {e}");
        }
    }

    public NativeAutoPlay(AutoPlayKind kind, Func<PlayTokenSelection, bool> allowRequest, Action stopNative)
    {
        if (!Ready) throw new InvalidOperationException("Native auto-play safety guards are unavailable.");
        if (_current != null) throw new InvalidOperationException("Another auto-play task still owns the controls.");
        _kind = kind;
        _allowRequest = allowRequest;
        _stopNative = stopNative;
        _current = this;
    }

    internal bool Allow(PlayTokenSelection selection)
    {
        if (Stopped) return false;
        try
        {
            if (_allowRequest(selection))
            {
                Requests++;
                return true;
            }
        }
        catch (Exception e)
        {
            Logger.Error($"[{_kind}] Auto request validation failed: {e.GetType().Name} - {e.Message}");
        }
        Stop();
        return false;
    }

    public void Stop()
    {
        Stopped = true;
        if (_stoppedCleanly) return;
        try
        {
            _stopNative();
            _stoppedCleanly = true;
        }
        catch (Exception e)
        {
            // Keep the guard installed and closed if the native toggle cannot be stopped.
            Logger.Error($"[{_kind}] Could not turn Auto off; further requests remain blocked. {e}");
        }
    }

    public void Dispose()
    {
        Stop();
        if (_stoppedCleanly && ReferenceEquals(_current, this)) _current = null;
    }

    internal static bool AwakeningPrefix(ref Il2CppSystem.Collections.IEnumerator __result)
        => BeforeRequest(AutoPlayKind.Awakening, default, ref __result);

    internal static bool ScarabStartPrefix(ScarabGameMech __instance,
        ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (_current == null || _current._kind != AutoPlayKind.Scarab) return true;
        var selection = __instance.currentPlayTokenSelection;
        if (!BeforeRequest(AutoPlayKind.Scarab, selection, ref __result)) return false;
        _current._scarabSelection = selection;
        _current._expectScarabSequence = true;
        return true;
    }

    internal static bool ScarabPrefix(PlayTokenSelection __0, ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (_current != null && _current._kind == AutoPlayKind.Scarab && _current._expectScarabSequence)
        {
            _current._expectScarabSequence = false;
            if (!_current.Stopped && __0.SelectedCurrency == _current._scarabSelection.SelectedCurrency &&
                __0.TokensNeeded == _current._scarabSelection.TokensNeeded && __0.HasEnoughTokens) return true;
            Logger.Warning("[Scarab] Native token selection changed before the spin; stopping.");
            _current.Stop();
            __result = new Il2CppSystem.Collections.ArrayList().GetEnumerator();
            return false;
        }
        return BeforeRequest(AutoPlayKind.Scarab, __0, ref __result);
    }

    private static bool BeforeRequest(AutoPlayKind kind, PlayTokenSelection selection,
        ref Il2CppSystem.Collections.IEnumerator result)
    {
        if (_current == null || _current._kind != kind || _current.Allow(selection)) return true;
        result = new Il2CppSystem.Collections.ArrayList().GetEnumerator();
        return false;
    }
}

internal sealed class AutoPlayReceipt
{
    private Func<double> _readBalance;
    private double _before;
    private double _cost;
    private int _quantity;
    public bool Pending => _readBalance != null;
    public int Confirmed { get; private set; }
    public bool Invalid { get; private set; }

    public static bool Affordable(double balance, double cost) =>
        double.IsFinite(balance) && double.IsFinite(cost) && cost > 0 && balance >= cost &&
        Math.Truncate(balance) == balance && Math.Truncate(cost) == cost;

    public void Expect(Func<double> readBalance, double before, double cost, int quantity)
    {
        if (Pending || !Affordable(before, cost) || quantity <= 0)
            throw new InvalidOperationException("Invalid or overlapping auto-play purchase.");
        _readBalance = readBalance;
        _before = before;
        _cost = cost;
        _quantity = quantity;
    }

    public bool Observe()
    {
        if (Invalid) return false;
        if (!Pending) return true;
        var after = _readBalance();
        if (after == _before) return false;
        if (!double.IsFinite(after) || after < 0 || after != _before - _cost)
        {
            Invalid = true;
            Logger.Warning("[Auto play] The expected currency decrease was not confirmed; stopping.");
            return false;
        }
        Confirmed += _quantity;
        _readBalance = null;
        return true;
    }
}
