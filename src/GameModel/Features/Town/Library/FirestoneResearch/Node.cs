using System;
using System.Collections;
using System.Linq;
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

    public IEnumerator Select(int id, Action<ResearchSelectionResult> selected)
    {
        var tree = GetTree();
        const string prefix = "firestoneResearch";
        var nodes = tree?.GetChildren()
            .Select(node => (node, name: node.Name))
            .Where(item => item.name.StartsWith(prefix, StringComparison.Ordinal) &&
                           int.TryParse(item.name.Substring(prefix.Length), out _))
            .OrderBy(item => int.Parse(item.name.Substring(prefix.Length)))
            .Select(item => item.node)
            .ToArray();
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