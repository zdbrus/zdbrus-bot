using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebot.Core;
using Firebot.GameModel.Base;
using Firebot.GameModel.Primitives;
using Firebot.Infrastructure;

namespace Firebot.GameModel.Features.Town.Library.FirestoneResearch;

public enum ResearchSelectionResult
{
    Unavailable,
    Selected,
    Failed
}

public class Node : GameElement
{
    public Node() : base(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.NodeLoc.Root) { }

    private GameElement GetTree() => GetChildren().FirstOrDefault(tree => tree.IsVisible());

    private static GameElement GetGrow(GameElement gameElement) =>
        new(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.NodeLoc.Glow, gameElement);

    private static GameText GetCompletedTxt(GameElement gameElement) =>
        new(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.NodeLoc.CompletedTxt, gameElement);

    private static GameElement GetProgressBar(GameElement gameElement) =>
        new(Paths.MenusLoc.CanvasLoc.TownLoc.LibraryLoc.NodeLoc.ProgressBar, gameElement);

    private static bool IsActiveNode(GameElement child)
    {
        if (!child.IsVisible()) return false;

        var grow = GetGrow(child);
        if (grow.IsVisible()) return false;

        var completedTxt = GetCompletedTxt(child);
        if (completedTxt.IsVisible()) return false;

        var progressBar = GetProgressBar(child);
        return progressBar.IsVisible();
    }

    private static bool _orderLogged;

    /// <summary>
    ///     The numbers in the priority list count nodes top to bottom, left to right, as the player sees them. The
    ///     game's own object names do not always follow that (tree 2 moves its nodes around, issue #10), so the nodes
    ///     are numbered by where they sit. If positions cannot be read the name order is kept.
    /// </summary>
    internal static GameElement[] OrderOnScreen(GameElement[] byName)
    {
        if (byName == null || byName.Length < 2) return byName;
        var spots = new (float x, float y, float h)[byName.Length];
        for (var i = 0; i < byName.Length; i++)
            if (!byName[i].TryGetScreenPosition(out spots[i].x, out spots[i].y, out spots[i].h))
                return byName;

        var order = ScreenOrder(spots);
        if (order == null) return byName;

        var sorted = order.Select(i => byName[i]).ToArray();
        Logger.Debug("[Firestone Research] Node numbers 1 to " + sorted.Length + " are, in order: " +
                     string.Join(", ", sorted.Select(node => node.Name)));
        if (!_orderLogged && order.Where((value, index) => value != index).Any())
        {
            _orderLogged = true;
            Logger.Info("[Firestone Research] This tree's node numbers follow the screen (top to bottom, left to " +
                        "right), not the game's object names.");
        }

        return sorted;
    }

    /// <summary>
    ///     Indexes of the given spots, rows top to bottom and left to right inside a row. Null when the spots do not
    ///     spread out (nothing to sort by).
    /// </summary>
    internal static int[] ScreenOrder((float x, float y, float h)[] spots)
    {
        if (spots == null || spots.Length < 2) return null;
        var minY = spots.Min(s => s.y);
        var maxY = spots.Max(s => s.y);
        var minX = spots.Min(s => s.x);
        var maxX = spots.Max(s => s.x);
        if (maxY - minY < 1e-4f && maxX - minX < 1e-4f) return null;

        // Two nodes are on the same row when they are closer than a third of a node's height.
        var height = spots.Where(s => s.h > 0).Select(s => s.h).DefaultIfEmpty(0f).Average();
        var tolerance = height > 0 ? height / 3f : (maxY - minY) / (spots.Length * 3f);
        var byTop = Enumerable.Range(0, spots.Length).OrderByDescending(i => spots[i].y).ToArray();
        var rows = new List<List<int>>();
        var rowY = float.NaN;
        foreach (var i in byTop)
        {
            if (rows.Count == 0 || Math.Abs(rowY - spots[i].y) > tolerance)
            {
                rows.Add(new List<int>());
                rowY = spots[i].y;
            }

            rows[^1].Add(i);
        }

        return rows.SelectMany(row => row.OrderBy(i => spots[i].x)).ToArray();
    }

    public IEnumerator Select(int id, Action<ResearchSelectionResult> selected)
    {
        var tree = GetTree();
        const string prefix = "firestoneResearch";
        var nodes = OrderOnScreen(tree?.GetChildren()
            .Select(node => (node, name: node.Name))
            .Where(item => item.name.StartsWith(prefix, StringComparison.Ordinal) &&
                           int.TryParse(item.name.Substring(prefix.Length), out _))
            .OrderBy(item => int.Parse(item.name.Substring(prefix.Length)))
            .Select(item => item.node)
            .ToArray());
        var child = nodes != null && id > 0 && id <= nodes.Length ? nodes[id - 1] : null;
        if (child == null || !IsActiveNode(child))
        {
            selected(ResearchSelectionResult.Unavailable);
            yield break;
        }

        yield return new GameButton(parent: child).ClickUntil(() => Preview.IsVisible,
            opened => selected(opened ? ResearchSelectionResult.Selected : ResearchSelectionResult.Failed),
            $"Firestone research {id} preview");
    }

    public IEnumerator SelectAny(Action<ResearchSelectionResult> selected)
    {
        var tree = GetTree();
        var child = tree?.GetChildren().FirstOrDefault(node =>
            node.Name.StartsWith("firestoneResearch", StringComparison.Ordinal) && IsActiveNode(node));

        if (child == null)
        {
            selected(ResearchSelectionResult.Unavailable);
            yield break;
        }

        yield return new GameButton(parent: child).ClickUntil(() => Preview.IsVisible,
            opened => selected(opened ? ResearchSelectionResult.Selected : ResearchSelectionResult.Failed),
            "Firestone research preview");
    }
}