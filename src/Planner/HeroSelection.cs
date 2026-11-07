using System;
using System.Collections;
using Firebot.Behaviors.Extra;
using Il2Cpp;
using UnityEngine;

namespace Firebot.Planner;

internal static class HeroSelection
{
    private const string Hall = P.Menus + "/HallOfHeroes";

    private static bool IsUnlocked(int hero)
    {
        var deck = GameInitialize.HandlerLoader?.heroDeck?.heroes;
        if (deck == null) return false;
        foreach (var entry in deck)
            if (entry != null && entry.HeroCode == hero && entry.IsUnlocked().GetDecrypted()) return true;
        return false;
    }

    internal static IEnumerator Select(HallOfHeroesMech mech, int hero, Func<bool> canContinue, Action<bool> result)
    {
        var until = Time.unscaledTime + 8f;
        var requested = false;
        while (mech != null && Time.unscaledTime < until && canContinue() &&
               GameInitialize.HasGameLoaded() && IsUnlocked(hero) && Ui.Visible(Hall))
        {
            if (!mech.heroSelectionLoading)
            {
                if (mech.heroServerSelected?.code == hero)
                {
                    result(true);
                    yield break;
                }
                if (!requested)
                {
                    var tiles = mech.heroInteractions;
                    for (var i = 0; tiles != null && i < tiles.Count; i++)
                    {
                        var tile = tiles[i];
                        if (tile?.hero?.code != hero || tile.IsEmpty() || tile.actionOnClick == null) continue;
                        // Use the tile's bound player action, not its position in a reordered hero list.
                        tile.InvokeAction();
                        requested = true;
                        break;
                    }
                }
            }
            yield return new WaitForSeconds(0.2f);
        }
        result(false);
    }
}
