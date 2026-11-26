using System;
using System.Collections;
using System.Collections.Generic;
using Firebot.Behaviors.Extra;
using Firebot.Core;
using Firebot.Core.Tasks;
using Firebot.GameModel.Features.Guild.Shop;
using Firebot.GameModel.Shared;
using MelonLoader;

namespace Firebot.Behaviors.Guild;

public class FreePickaxesTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.Guilds;

    private MelonPreferences_Entry<int> _pickaxeClaimThreshold;

    /// <summary>
    ///     The longest time-to-next-pickaxe seen so far: the closest reading to the game's full interval. It can
    ///     only under-estimate the interval, so the bot comes back early rather than late.
    /// </summary>
    private TimeSpan _intervalEstimate = TimeSpan.Zero;

    private int PickaxeClaimThreshold => _pickaxeClaimThreshold?.Value ?? 1;
    internal override IEnumerable<string> HandledNotificationNames => new[] { "FreePickaxes" };

    protected override bool IsNotificationVisibleCore()
        => IsEnabled && DateTime.Now >= BubbleQuietUntil && Ui.Visible(P.Notifications + "/FreePickaxes");

    public override int MinimumLevel => LevelRequirements.FreePickaxes;

    protected override void OnConfigure(MelonPreferences_Category category)
    {
        if (_pickaxeClaimThreshold != null) return;

        _pickaxeClaimThreshold = category.CreateEntry(
            "pickaxe_claim_threshold",
            30,
            "Pickaxe Claim Threshold",
            "Minimum number of free pickaxes required before claiming. " +
            "Set to 1 to claim as soon as available, or up to 30 to wait for maximum. " +
            "Default is 30 (wait for maximum)."
        );
    }

    private void RetryInTenMinutes()
    {
        var retryAt = DateTime.Now.AddMinutes(10);
        NextRunTime = retryAt;
        if (BubbleQuietUntil < retryAt) BubbleQuietUntil = retryAt;
    }

    public override IEnumerator Execute()
    {
        yield return Notifications.FreePickaxes;

        if (!FreePickaxes.TryGetQuantity(out var before))
        {
            Logger.Warning("[Free Pickaxes] The available quantity could not be read; trying again later.");
            RetryInTenMinutes();
            yield return GuildShop.Close;
            yield break;
        }

        if (before >= PickaxeClaimThreshold)
        {
            var confirmed = false;
            const string claimButton =
                P.Menus + "/GuildShop/bg/submenus/supplies/items/freePickaxe/claimBg/purchaseButton";
            const string claimedMarker =
                P.Menus + "/GuildShop/bg/submenus/supplies/items/freePickaxe/claimBg/claimedBG";
            var wasClaimed = Ui.Visible(claimedMarker);
            yield return Ui.ClickUntil(Ui.Find(claimButton),
                () => (FreePickaxes.TryGetQuantity(out var after) && after < before) ||
                      (!wasClaimed && Ui.Visible(claimedMarker)),
                value => confirmed = value, "Free pickaxes claim", 1.5f);
            if (confirmed)
                Logger.Info("[Free Pickaxes] The claim was confirmed by the game.");
            else
            {
                Logger.Warning("[Free Pickaxes] The claim could not be confirmed; trying again later.");
                RetryInTenMinutes();
                yield return GuildShop.Close;
                yield break;
            }
        }
        else
        {
            // Below the threshold the bubble is lit for every single pickaxe. Come back when enough should have
            // built up, instead of opening the shop for each one (issue #8).
            var at = PickaxePlan.NextCheck(DateTime.Now, FreePickaxes.NextRunTime, before, PickaxeClaimThreshold,
                ref _intervalEstimate);
            NextRunTime = at;
            if (BubbleQuietUntil < at) BubbleQuietUntil = at;
            Logger.Info($"[Free Pickaxes] Waiting for {PickaxeClaimThreshold} pickaxes; {before} available. " +
                        $"Checking again at {at:HH:mm}.");
            yield return GuildShop.Close;
            yield break;
        }

        NextRunTime = FreePickaxes.NextRunTime;
        yield return GuildShop.Close;
    }
}
/// <summary>When to look at the free pickaxes again while waiting for the claim threshold.</summary>
internal static class PickaxePlan
{
    public static DateTime NextCheck(DateTime now, DateTime nextPickaxe, int have, int threshold, ref TimeSpan interval)
    {
        var untilNext = nextPickaxe > now ? nextPickaxe - now : TimeSpan.Zero;
        if (untilNext > interval && untilNext < TimeSpan.FromDays(1)) interval = untilNext;
        if (untilNext <= TimeSpan.Zero) return now.AddMinutes(10);

        var needed = Math.Max(1, threshold - have);
        var at = nextPickaxe + TimeSpan.FromTicks(interval.Ticks * (needed - 1));
        var latest = now.AddHours(12);
        return at > latest ? latest : at;
    }
}
