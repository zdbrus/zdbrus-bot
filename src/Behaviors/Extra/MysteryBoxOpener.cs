using System.Collections;
using Firebot.Utilities;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

internal static class MysteryBoxOpener
{
    internal const string ItemId = "mysteryBox";
    private const string Preview = P.Popups + "/MysteryBoxOpenPreview/bg";
    private const string Opening = P.Menus + "/ChestOpening";
    private const string NewItems = P.Popups + "/NewItem";

    private static bool TryReadCount(string content, out long count)
    {
        count = 0;
        if (!Ui.Visible(content)) return false;
        var box = Ui.Find(content + "/" + ItemId);
        if (box == null || !box.gameObject.activeInHierarchy) return true;
        return StringUtils.TryParseCount(Ui.Text(box.Find("quantity")), out count);
    }

    public static IEnumerator Open(string content)
    {
        if (!TryReadCount(content, out var before))
        {
            Logger.Warning("[Chests] Mystery Box quantity could not be read; keeping them for now.");
            yield break;
        }
        if (before == 0) yield break;
        var box = Ui.Find(content + "/" + ItemId);
        if (!Ui.Clickable(box))
        {
            Logger.Warning("[Chests] Mystery Boxes could not be selected; keeping them for now.");
            yield break;
        }

        yield return Ui.Click(box, 1.2f);
        yield return Ui.WaitFor(Preview, 2f);
        if (!Ui.Visible(Preview))
        {
            Ui.PointerClick(box);
            yield return Ui.WaitFor(Preview, 4f);
        }
        if (!Ui.Visible(Preview))
        {
            Logger.Warning("[Chests] The Mystery Box preview did not open; keeping them for now.");
            yield break;
        }

        if (Ui.Visible(Preview + "/chestRequirementBg"))
        {
            Logger.Warning("[Chests] Mystery Boxes show an opening requirement; keeping them.");
            yield return Ui.Click(Preview + "/closeButton", 0.6f);
            yield break;
        }

        Transform open = null;
        string openPath = null;
        for (var attempt = 0; attempt < 20 && open == null; attempt++)
        {
            foreach (var option in new[] { "openxMax", "openx10", "openx1" })
            {
                var path = Preview + "/openingOptions/" + option;
                var button = Ui.Find(path);
                var icon = button?.Find("chestIcon");
                if (Ui.Clickable(button) && icon != null && icon.gameObject.activeInHierarchy &&
                    !Ui.HasPriceMarker(button))
                {
                    open = button;
                    openPath = path;
                    break;
                }
            }
            if (open == null) yield return new WaitForSeconds(0.25f);
        }

        if (open == null)
        {
            Logger.Info("[Chests] Mystery Boxes have no safe free opening option right now; keeping them.");
            yield return Ui.Click(Preview + "/closeButton", 0.6f);
            yield break;
        }

        if (!StringUtils.TryParseCount(Ui.Text(open.Find("quantity")), out var quantity) ||
            quantity <= 0 || quantity > before)
        {
            Logger.Warning("[Chests] The Mystery Box opening amount is unreadable or exceeds the owned count; keeping them.");
            yield return Ui.Click(Preview + "/closeButton", 0.6f);
            yield break;
        }

        var clicked = false;
        yield return Ui.ClickIf(open,
            () => Ui.Find(openPath) == open && Ui.Visible(Preview) &&
                  !Ui.Visible(Preview + "/chestRequirementBg") &&
                  TryReadCount(content, out var currentCount) && currentCount == before &&
                  open.Find("chestIcon") is { } icon && icon.gameObject.activeInHierarchy &&
                  !Ui.HasPriceMarker(open) &&
                  StringUtils.TryParseCount(Ui.Text(open.Find("quantity")), out var currentQuantity) &&
                  currentQuantity == quantity,
            value => clicked = value, 2f, $"open {quantity:0} Mystery Box(es)");
        if (!clicked)
        {
            Logger.Warning("[Chests] Mystery Box state changed before opening; keeping them.");
            if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.6f);
            yield break;
        }
        if (Ui.MissingCurrency)
        {
            if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.6f);
            yield break;
        }

        var until = Time.unscaledTime + 10f;
        while (Time.unscaledTime < until && !Ui.Visible(P.RewardsClose))
        {
            if (Ui.Clickable(Opening + "/closeButton")) { yield return Ui.Click(Opening + "/closeButton", 1f); break; }
            if (Ui.Clickable(Opening + "/background")) yield return Ui.Click(Opening + "/background", 0.5f);
            else yield return new WaitForSeconds(0.5f);
        }
        yield return Ui.CloseRewards();
        if (Ui.Visible(Preview)) yield return Ui.Click(Preview + "/closeButton", 0.6f);
        for (var n = 0; n < 5 && Ui.Visible(NewItems); n++)
            yield return Ui.Click(NewItems + "/bg/closeButton", 1f);

        var confirmed = false;
        var after = before;
        var deadline = Time.unscaledTime + 5f;
        while (Time.unscaledTime < deadline)
        {
            if (TryReadCount(content, out after) && after < before)
            {
                confirmed = true;
                break;
            }
            yield return new WaitForSeconds(0.25f);
        }

        if (confirmed)
            Logger.Info($"[Chests] Opened {before - after} Mystery Box(es); {after} left.");
        else
            Logger.Warning("[Chests] Mystery Box opening could not be confirmed; will check again later.");
    }
}
