using System.Collections.Generic;
using Firebot.Core;
using Firebot.UI.Config;
using UnityEngine;

namespace Firebot.UI.Widgets;

/// <summary>
///     Multiple selection for string entries with options declared in ConfigRegistry.MultiChoices.
///     <para>
///         The widget is only a naming layer: the .cfg still stores "0,1" and the task keeps reading
///         the same format it already read. Changing the option set means editing the registry
///         dictionary, not this file.
///     </para>
/// </summary>
internal sealed class MultiSelectRow : ChipRow
{
    private readonly List<string> _selected = new();
    private readonly bool _ordered;

    public MultiSelectRow(ConfigEntry config, RectTransform rowRect) : base(config, rowRect)
    {
        // The state must be read before Build, and the base constructor runs before this body.
        _ordered = config.IsOrderedSelection;
        _selected.AddRange(config.ReadSelection());

        Build();
    }

    internal const string ClearId = "__clear";
    internal const string RecommendedId = "__recommended";

    private static readonly ConfigChoice[] OrderedActions =
    {
        new(ClearId, "Clear"),
        new(RecommendedId, "Recommended")
    };

    protected override ConfigChoice[] ActionChoices => _ordered ? OrderedActions : base.ActionChoices;

    private static bool IsAction(string id) => id is ClearId or RecommendedId;

    protected override bool IsOn(string id) => !IsAction(id) && _selected.Contains(id);

    protected override string DisplayName(string id, string name)
    {
        var index = _ordered && !IsAction(id) ? _selected.IndexOf(id) : -1;
        return index >= 0 ? $"{index + 1}. {name}" : name;
    }

    protected override string MeasuredName(string id, string name) =>
        _ordered && !IsAction(id) ? "999. " + name : name;

    protected override void ApplySelection(string id)
    {
        RecommendedOrders.ApplyChip(_selected, id, ClearId, RecommendedId, Config.DefaultText());
        Config.Write(Config.EncodeSelection(_selected));
    }
}
