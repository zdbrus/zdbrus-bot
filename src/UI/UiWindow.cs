using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Firebot.UI;

/// <summary>
///     Screen chrome shared by both windows: isolated canvas, input blocker, centred window and a
///     header with title and close button.
///     <para>
///         It exists so the screens do not drift apart. Without it, every new screen would repeat
///         the same set of anchors — and, over time, with slightly different numbers.
///     </para>
/// </summary>
internal sealed class UiWindow
{
    private UiWindow(GameObject root, Transform window, RectTransform header, RectTransform content)
    {
        Root = root;
        Window = window;
        Header = header;
        Content = content;
    }

    /// <summary>Root object, with the Canvas. Doubles as a lifetime handle: the scene destroys it.</summary>
    public GameObject Root { get; }

    /// <summary>Window transform, for hanging content that is not text rows.</summary>
    public Transform Window { get; }

    /// <summary>Top band. Exposed so a screen can hang indicators next to the title.</summary>
    public RectTransform Header { get; }

    /// <summary>Usable area: below the header, with the side paddings already applied.</summary>
    public RectTransform Content { get; }

    /// <summary>
    ///     False once a scene change destroyed the objects. Comparing the GameObject with null works
    ///     because Object.op_Equality can tell "destroyed" apart from "never existed".
    /// </summary>
    public bool IsAlive => Root != null;

    public void SetActive(bool value)
    {
        if (Root != null) Root.SetActive(value);
    }

    public static UiWindow Create(string name, string title, float width, float height, int sortingOrder,
        Action onClose, float contentBottomInset = 0f)
    {
        var root = UiFactory.CreateCanvas(name, sortingOrder);

        // The blocker is what keeps a click from reaching the game while the window is open.
        var blocker = UiFactory.CreateImage("blocker", root.transform, UiTheme.Blocker, true);
        UiFactory.Stretch(blocker.rectTransform);

        var window = UiFactory.CreateImage("window", root.transform, UiTheme.Window, true);
        UiFactory.Center(window.rectTransform, width, height, Vector2.zero);

        var header = BuildHeader(window.transform, title, onClose);
        BuildHudTrim(window.rectTransform);

        var content = UiFactory.CreateNode("content", window.transform);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = new Vector2(UiTheme.Padding, UiTheme.Padding + contentBottomInset);
        content.offsetMax = new Vector2(-UiTheme.Padding, -(UiTheme.HeaderHeight + UiTheme.ContentTopGap));

        return new UiWindow(root, window.transform, header, content);
    }

    /// <summary>Command-screen trim: a glowing cyan line under the header, cyan and orange corner brackets.</summary>
    private static void BuildHudTrim(RectTransform window)
    {
        Strip("hudHeaderGlow", window, UiTheme.CyanGlow, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -UiTheme.HeaderHeight - 6f), new Vector2(0f, -UiTheme.HeaderHeight));
        Strip("hudHeaderLine", window, UiTheme.Cyan, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -UiTheme.HeaderHeight - UiTheme.GlowLine), new Vector2(0f, -UiTheme.HeaderHeight));
        Strip("hudTL1", window, UiTheme.Cyan, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -3f), new Vector2(UiTheme.Bracket, 0f));
        Strip("hudTL2", window, UiTheme.Cyan, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -UiTheme.Bracket), new Vector2(3f, 0f));
        Strip("hudTRGlow", window, UiTheme.OrangeGlow, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-UiTheme.Bracket - 5f, -5f), new Vector2(0f, 0f));
        Strip("hudBLGlow", window, UiTheme.CyanGlow, Vector2.zero, Vector2.zero,
            new Vector2(0f, 0f), new Vector2(UiTheme.Bracket + 5f, 5f));
        Strip("hudBL1", window, UiTheme.Cyan, Vector2.zero, Vector2.zero, new Vector2(0f, 0f), new Vector2(UiTheme.Bracket, 3f));
        Strip("hudBL2", window, UiTheme.Cyan, Vector2.zero, Vector2.zero, new Vector2(0f, 0f), new Vector2(3f, UiTheme.Bracket));
        Strip("hudBRGlow", window, UiTheme.OrangeGlow, new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-UiTheme.Bracket - 5f, -3f), new Vector2(0f, 3f));
        Strip("hudBR1", window, UiTheme.Accent, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-UiTheme.Bracket, 0f), new Vector2(0f, 3f));
        Strip("hudBR2", window, UiTheme.Accent, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-3f, 0f), new Vector2(0f, UiTheme.Bracket));
        Strip("hudTR1", window, UiTheme.Accent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-UiTheme.Bracket, -3f), new Vector2(0f, 0f));
        Strip("hudTR2", window, UiTheme.Accent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-3f, -UiTheme.Bracket), new Vector2(0f, 0f));
    }

    private static void Strip(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        var img = UiFactory.CreateImage(name, parent, color, false);
        var r = img.rectTransform;
        r.anchorMin = anchorMin;
        r.anchorMax = anchorMax;
        r.offsetMin = offsetMin;
        r.offsetMax = offsetMax;
    }

    private static RectTransform BuildHeader(Transform window, string title, Action onClose)
    {
        var header = UiFactory.CreateImage("header", window, UiTheme.Header, true);
        UiFactory.PlaceTop(header.rectTransform, 0f, UiTheme.HeaderHeight);

        var label = UiFactory.CreateLabel(header.transform, title, UiTheme.TitleFontSize, UiTheme.Text,
            TextAlignmentOptions.Left);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(UiTheme.HeaderInsetX, 0f);
        labelRect.offsetMax = new Vector2(
            -(UiTheme.CloseButtonSize + 2f * UiTheme.CloseButtonMargin), 0f);

        // The vertical position is derived, not copied from the margin: with the old size (52) the
        // arithmetic happened to give exactly 12, and the button only looked centred by coincidence.
        // Deriving it keeps the button centred at any size.
        var closeVertical = -(UiTheme.HeaderHeight - UiTheme.CloseButtonSize) / 2f;

        var close = UiFactory.CreateButton("closeButton", header.transform, "X", UiTheme.Danger, onClose,
            UiTheme.CloseButtonFontSize);
        UiFactory.AnchorTopRight(close.GetComponent<RectTransform>(), UiTheme.CloseButtonSize,
            UiTheme.CloseButtonSize, new Vector2(-UiTheme.CloseButtonMargin, closeVertical));

        return header.rectTransform;
    }
}
