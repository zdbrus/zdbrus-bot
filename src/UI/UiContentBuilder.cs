using System;
using System.Collections.Generic;
using Firebot.Core;
using Firebot.UI.Config;
using Firebot.UI.Widgets;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Logger = Firebot.Core.Logger;

namespace Firebot.UI;

/// <summary>
///     Builds the panel rows from the preferences registry, in collapsible sections.
///     It does not use VerticalLayoutGroup: every block has a fixed height and is positioned by a
///     cursor. Collapsing neither destroys nor recreates anything — it only repositions and
///     deactivates, so the state of the controls (and any values already typed) survives
///     opening and closing a section.
/// </summary>
internal static class UiContentBuilder
{
    private sealed class Block
    {
        public RectTransform Rect;
        public float Height;
        public int Section;
        public bool AlwaysVisible;
    }

    private readonly struct Detail
    {
        public Detail(string title, string body)
        {
            Title = title;
            Body = body;
        }

        public string Title { get; }
        public string Body { get; }
    }

    /// <summary>
    ///     Tabs, in order. A section lands in the first tab whose words appear in its title (spaces ignored);
    ///     anything new and unmatched goes to "Other", so a new task never disappears from the panel.
    /// </summary>
    private static readonly (string Name, string[] Keys)[] TabDefs =
    {
        ("Bot", new[] { "general", "autoskill", "autoupgrade" }),
        ("Daily", new[] { "dailyrewards", "quests", "mail", "battlepass", "bubblerewards", "openchests" }),
        ("Town", new[] { "alchemist", "firestoneresearch", "meteoriteresearch", "magicquarters", "oracle", "engineertools",
            "exoticmerchant", "tavern", "scarab", "pharaoh" }),
        ("Guild", new[] { "expedition", "freepickaxes", "arcanecrystal", "chaosrift", "personaltree", "awakenheroes",
            "forbiddenknowledge" }),
        ("Battle", new[] { "mapmissions", "warfront", "liberation", "arena", "warmachine", "bossretry" }),
        ("Events", new[] { "event" }),
        ("Progress", new[] { "talents", "templeprestige", "prestige" }),
        ("Other", new string[0])
    };

    private static readonly List<int> SectionTab = new();
    private static readonly List<int> UsedTabs = new();
    private static readonly List<Image> TabImages = new();
    private static readonly List<TMP_Text> TabLabels = new();
    private static int _currentTab = -1;

    private static readonly List<Block> Blocks = new();
    private static readonly List<bool> Expanded = new();
    private static readonly List<TMP_Text> Markers = new();

    /// <summary>
    ///     Text that did not fit the description box, already reported. The panel is rebuilt on every scene
    ///     change while the problem is in the declaration, not in the build.
    /// </summary>
    private static readonly HashSet<string> ReportedLongTexts = new();

    // Description under the cursor. The key is the row's InstanceID: the Il2CppInterop wrappers have
    // no stable hash by reference, so comparing Transform inside a dictionary would not work.
    private static readonly Dictionary<int, Detail> Details = new();
    private static readonly Il2CppSystem.Collections.Generic.List<RaycastResult> Hits = new();

    private static RectTransform _rows;
    private static GraphicRaycaster _raycaster;
    private static Action<string, string> _describe;
    private static PointerEventData _pointer;
    private static Vector3 _lastPointer;
    private static int _shownRowId = int.MinValue;
    private static bool _raycasterMissingLogged;
    private static float _lastHoverReport;

    public static void Build(RectTransform rows, GraphicRaycaster raycaster, Action<string, string> describe)
    {
        _rows = rows;
        _raycaster = raycaster;
        _describe = describe;
        _shownRowId = int.MinValue;
        _lastPointer = Vector3.zero;

        Blocks.Clear();
        SectionTab.Clear();
        Expanded.Clear();
        Markers.Clear();
        Details.Clear();

        var groups = ConfigRegistry.Read();
        var rowCount = 0;

        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var section = Expanded.Count;

            // Sections start closed; a tab with only a few of them opens them all (see below).
            Expanded.Add(false);
            SectionTab.Add(TabFor(group.Title));

            AddSectionHeader(group, section);

            for (var i = 0; i < group.Entries.Count; i++)
            {
                AddRow(group.Entries[i], section, group.Title);
                rowCount++;
            }
        }

        UsedTabs.Clear();
        for (var t = 0; t < TabDefs.Length; t++)
        {
            var count = 0;
            for (var s = 0; s < SectionTab.Count; s++) if (SectionTab[s] == t) count++;
            if (count == 0) continue;
            UsedTabs.Add(t);
            // A short tab shows everything at once; a long one stays a list of headers to pick from.
            if (count <= 4)
                for (var s = 0; s < SectionTab.Count; s++) if (SectionTab[s] == t) Expanded[s] = true;
        }
        if (_currentTab < 0 || !UsedTabs.Contains(_currentTab)) _currentTab = UsedTabs.Count > 0 ? UsedTabs[0] : 0;

        Relayout();
        Logger.Debug($"[UI] Panel content: {groups.Count} section(s), {rowCount} setting(s), {UsedTabs.Count} tab(s).");
    }

    private static int TabFor(string title)
    {
        var key = (title ?? "").Replace(" ", "").ToLowerInvariant();
        for (var t = 0; t < TabDefs.Length; t++)
            foreach (var k in TabDefs[t].Keys)
                if (key.Contains(k)) return t;
        return TabDefs.Length - 1;
    }

    /// <summary>One button per tab that has sections, sharing the bar's width.</summary>
    public static void BuildTabs(RectTransform bar)
    {
        TabImages.Clear();
        TabLabels.Clear();
        var n = Mathf.Max(1, UsedTabs.Count);
        for (var i = 0; i < UsedTabs.Count; i++)
        {
            var tab = UsedTabs[i];
            var button = UiFactory.CreateButton($"tab_{TabDefs[tab].Name}", bar, TabDefs[tab].Name.ToUpperInvariant(),
                UiTheme.TabIdle, () => SelectTab(tab), UiTheme.TabFontSize);
            var r = button.GetComponent<RectTransform>();
            r.anchorMin = new Vector2((float)i / n, 0f);
            r.anchorMax = new Vector2((float)(i + 1) / n, 1f);
            r.offsetMin = new Vector2(i == 0 ? 0f : UiTheme.TabGap / 2f, 0f);
            r.offsetMax = new Vector2(i == n - 1 ? 0f : -UiTheme.TabGap / 2f, 0f);
            TabImages.Add(button.GetComponent<Image>());
            TabLabels.Add(button.GetComponentInChildren<TMP_Text>());

            var line = UiFactory.CreateImage("active", button.transform, UiTheme.Cyan, false);
            var lr = line.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(1f, 0f);
            lr.offsetMin = new Vector2(0f, 0f);
            lr.offsetMax = new Vector2(0f, UiTheme.GlowLine + 1f);
        }
        PaintTabs();
    }

    private static void SelectTab(int tab)
    {
        if (_currentTab == tab) return;
        _currentTab = tab;
        PaintTabs();
        Relayout();
        if (_rows != null) _rows.anchoredPosition = Vector2.zero; // a new tab starts at the top
    }

    private static void PaintTabs()
    {
        for (var i = 0; i < TabImages.Count && i < UsedTabs.Count; i++)
        {
            var active = UsedTabs[i] == _currentTab;
            if (TabImages[i] != null)
            {
                TabImages[i].color = active ? UiTheme.TabActive : UiTheme.TabIdle;
                var line = TabImages[i].transform.Find("active");
                if (line != null) line.gameObject.SetActive(active);
            }
            if (TabLabels[i] != null) TabLabels[i].color = active ? UiTheme.Cyan : UiTheme.TextMuted;
        }
    }

    private static void AddSectionHeader(ConfigGroup group, int section)
    {
        var header = UiFactory.CreateImage($"section_{group.Category.Identifier}", _rows, UiTheme.SectionHeader, true);

        var button = header.gameObject.AddComponent<Button>();
        button.targetGraphic = header;
        button.onClick.AddListener((Action)(() => ToggleSection(section)));

        var title = UiFactory.CreateLabel(header.transform, group.Title, UiTheme.SectionFontSize, UiTheme.Accent,
            TextAlignmentOptions.Left);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(UiTheme.SectionInsetLeft, 0f);
        titleRect.offsetMax = new Vector2(-UiTheme.SectionInsetRight, 0f);

        var marker = UiFactory.CreateLabel(header.transform, "-", UiTheme.SectionFontSize, UiTheme.TextMuted,
            TextAlignmentOptions.Right);
        var markerRect = marker.rectTransform;
        markerRect.anchorMin = Vector2.zero;
        markerRect.anchorMax = Vector2.one;
        markerRect.offsetMin = new Vector2(0f, 0f);
        markerRect.offsetMax = new Vector2(-UiTheme.SectionMarkerInset, 0f);

        Markers.Add(marker);

        Blocks.Add(new Block
        {
            Rect = header.rectTransform,
            Height = UiTheme.SectionHeight,
            Section = section,
            AlwaysVisible = true
        });
    }

    private static void AddRow(ConfigEntry config, int section, string sectionTitle)
    {
        var background = UiFactory.CreateImage($"row_{config.Entry.Identifier}", _rows, UiTheme.Row, true);
        var row = UiRow.Create(config, background.rectTransform);

        // The whole row is the target. ColorTint replaces the graphic's color, so the tints have to
        // carry the same low alpha as the background — otherwise the row would turn into an opaque
        // white band on hover.
        var button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        var colors = button.colors;
        colors.normalColor = UiTheme.Row;
        colors.highlightedColor = UiTheme.RowHighlight;
        colors.pressedColor = UiTheme.RowPressed;
        colors.selectedColor = UiTheme.Row;
        colors.disabledColor = UiTheme.Row;
        colors.fadeDuration = 0.06f;
        button.colors = colors;

        // The panel text, not the .cfg one: what the file stores is the comment MelonLoader writes, and it
        // is written for the file. The box holds about three lines and cuts the rest.
        var description = config.PanelText;
        ReportLongText(config, description);

        // The title is the section the row belongs to and not the setting name: the name is already on the
        // row, while the section header may be scrolled far above by the time the box is read.
        button.onClick.AddListener((Action)(() => _describe?.Invoke(sectionTitle, description)));

        Details[background.gameObject.GetInstanceID()] = new Detail(sectionTitle, description);

        Blocks.Add(new Block
        {
            Rect = background.rectTransform,
            Height = row?.Height ?? UiTheme.RowHeight,
            Section = section
        });
    }

    /// <summary>
    ///     Warns about a text the description box cannot show in full. The box truncates with an ellipsis,
    ///     which is silent by nature: the chip rows already warn when their options overflow the column, and
    ///     this is the same check for the text — an entry listed here needs an entry in
    ///     ConfigRegistry.PanelTexts.
    /// </summary>
    private static void ReportLongText(ConfigEntry config, string text)
    {
        if (!ReportedLongTexts.Add(config.Key)) return;

        // Wrapped the way the box wraps: the newlines are explicit, the rest is the estimate.
        var lines = 0;
        foreach (var segment in text.Split('\n'))
            lines += Mathf.Max(1, Mathf.CeilToInt(segment.Length / UiTheme.DetailCharsPerLine));

        if (lines <= UiTheme.DetailBodyLines) return;

        Logger.Warning(
            $"[UI] '{config.Key}': {text.Length} chars need about {lines} lines but the description box " +
            $"shows {UiTheme.DetailBodyLines}. Declare a shorter text in ConfigRegistry.PanelTexts.");
    }

    private static void ToggleSection(int section)
    {
        Expanded[section] = !Expanded[section];
        Relayout();
    }

    /// <summary>Repositions the visible blocks in sequence and recomputes the content height.</summary>
    private static void Relayout()
    {
        var y = 0f;

        for (var i = 0; i < Blocks.Count; i++)
        {
            var block = Blocks[i];
            var inTab = block.Section >= SectionTab.Count || SectionTab[block.Section] == _currentTab;
            var visible = inTab && (block.AlwaysVisible || Expanded[block.Section]);

            var go = block.Rect.gameObject;
            if (go.activeSelf != visible) go.SetActive(visible);

            if (!visible) continue;

            UiFactory.PlaceTop(block.Rect, y, block.Height);
            y += block.Height + UiTheme.RowSpacing;
        }

        for (var s = 0; s < Markers.Count && s < Expanded.Count; s++)
            if (Markers[s] != null)
                Markers[s].text = Expanded[s] ? "-" : "+";

        if (_rows != null) _rows.sizeDelta = new Vector2(0f, Mathf.Max(0f, y - UiTheme.RowSpacing));
    }

    /// <summary>
    ///     Finds which row is under the cursor and publishes its description.
    ///     Uses <em>our</em> canvas raycaster instead of EventSystem.RaycastAll, so it does not sweep
    ///     the whole game UI; and it only runs when the mouse has actually moved.
    ///     The text is not cleared when the cursor leaves: a long description has to stay readable.
    /// </summary>
    public static void Poll()
    {
        if (_rows == null) return;

        if (_raycaster == null)
        {
            if (!_raycasterMissingLogged)
            {
                _raycasterMissingLogged = true;
                Logger.Warning("[UI] No GraphicRaycaster on the panel canvas: hovering will not show descriptions.");
            }

            return;
        }

        var pointer = Input.mousePosition;
        if (pointer.x == _lastPointer.x && pointer.y == _lastPointer.y) return;
        _lastPointer = pointer;

        _pointer ??= new PointerEventData(EventSystem.current);
        _pointer.position = pointer;

        Hits.Clear();
        _raycaster.Raycast(_pointer, Hits);
        ReportHover();

        for (var i = 0; i < Hits.Count; i++)
        {
            var rowTransform = FindRowRoot(Hits[i].gameObject.transform);
            if (rowTransform == null) continue;

            // The key has to be the GameObject: GetInstanceID is per component, and the row's Image
            // has a different id from its Transform.
            var id = rowTransform.gameObject.GetInstanceID();
            if (!Details.TryGetValue(id, out var detail)) return;
            if (id == _shownRowId) return;

            _shownRowId = id;
            _describe?.Invoke(detail.Title, detail.Body);
            return;
        }
    }

    /// <summary>Walks up the hierarchy until it finds the direct child of _rows holding the hit transform.</summary>
    private static Transform FindRowRoot(Transform transform)
    {
        if (_rows == null) return null;

        var rowsId = _rows.GetInstanceID();
        var current = transform;

        while (current != null)
        {
            var parent = current.parent;
            if (parent == null) return null;
            if (parent.GetInstanceID() == rowsId) return current;

            current = parent;
        }

        return null;
    }

    /// <summary>
    ///     Hover report, only with debug_mode on. It exists because a failure here is silent by
    ///     nature: without it there is no way to tell "nothing was hit" from "something that is not
    ///     a row was hit".
    /// </summary>
    private static void ReportHover()
    {
        if (!BotSettings.DebugMode) return;
        if (Time.unscaledTime - _lastHoverReport < 1f) return;
        _lastHoverReport = Time.unscaledTime;

        if (Hits.Count == 0)
        {
            Logger.Debug("[UI] Hover: 0 hits under the pointer.");
            return;
        }

        var first = Hits[0].gameObject;
        var row = FindRowRoot(first.transform);
        var resolved = row != null ? row.gameObject.name : "<not a row>";

        Logger.Debug($"[UI] Hover: {Hits.Count} hit(s), first='{first.name}', row='{resolved}'");
    }
}
