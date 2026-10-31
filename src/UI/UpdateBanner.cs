using Firebot.Core;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Firebot.UI;

internal static class UpdateBanner
{
    private static RectTransform _viewport;
    private static RectTransform _textRect;
    private static TMP_Text _text;
    private static Image _dot;
    private static string _displayed;
    private static float _textWidth;
    private static float _elapsed;

    public static void Build(Transform parent)
    {
        var bar = UiFactory.CreateImage("updateBanner", parent, UiTheme.SectionHeader, false);
        UiFactory.PlaceTop(bar.rectTransform, UiTheme.HeaderHeight + UiTheme.ContentTopGap,
            UiTheme.UpdateBarHeight, UiTheme.Padding);

        _dot = UiFactory.CreateImage("updateStatus", bar.transform, UiTheme.TextMuted, false, UiTheme.CircleSprite);
        var dotRect = _dot.rectTransform;
        dotRect.anchorMin = new Vector2(0f, 0.5f);
        dotRect.anchorMax = new Vector2(0f, 0.5f);
        dotRect.pivot = new Vector2(0f, 0.5f);
        dotRect.sizeDelta = new Vector2(UiTheme.UpdateDotSize, UiTheme.UpdateDotSize);
        dotRect.anchoredPosition = new Vector2(UiTheme.UpdateInset, 0f);

        var releases = UiFactory.CreateButton("releases", bar.transform, "Releases", UiTheme.Button,
            () => Application.OpenURL(UpdateCheck.ReleasesUrl), UiTheme.UpdateFontSize);
        var buttonRect = releases.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 0.5f);
        buttonRect.anchorMax = new Vector2(1f, 0.5f);
        buttonRect.pivot = new Vector2(1f, 0.5f);
        buttonRect.sizeDelta = new Vector2(UiTheme.UpdateButtonWidth, UiTheme.UpdateButtonHeight);
        buttonRect.anchoredPosition = new Vector2(-UiTheme.UpdateInset, 0f);

        var viewport = UiFactory.CreateImage("updateViewport", bar.transform, UiTheme.Transparent, false);
        _viewport = viewport.rectTransform;
        _viewport.anchorMin = Vector2.zero;
        _viewport.anchorMax = Vector2.one;
        _viewport.offsetMin = new Vector2(UiTheme.UpdateInset * 2f + UiTheme.UpdateDotSize, 0f);
        _viewport.offsetMax = new Vector2(-(UiTheme.UpdateButtonWidth + UiTheme.UpdateInset * 2f), 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        _text = UiFactory.CreateLabel(_viewport, "", UiTheme.UpdateFontSize, UiTheme.Text, TextAlignmentOptions.Left);
        _textRect = _text.rectTransform;
        _textRect.anchorMin = Vector2.zero;
        _textRect.anchorMax = Vector2.up;
        _textRect.pivot = new Vector2(0f, 0.5f);
        _textRect.anchoredPosition = Vector2.zero;
        _displayed = null;
        Refresh();
    }

    public static void Reset()
    {
        _viewport = null;
        _textRect = null;
        _text = null;
        _dot = null;
        _displayed = null;
    }

    public static void Refresh()
    {
        if (_text == null || _viewport == null) return;

        var message = UpdateStatusPolicy.Describe(UpdateCheck.Status, UpdateCheck.Newer, Brand.Version);
        if (_displayed != message)
        {
            _displayed = message;
            _text.text = message;
            _textWidth = _text.preferredWidth;
            if (_textWidth <= 0f) _textWidth = message.Length * UiTheme.UpdateFontSize * UiTheme.ChipCharRatio;
            _textRect.sizeDelta = new Vector2(_textWidth + 4f, 0f);
            _elapsed = 0f;
        }

        _dot.color = UpdateCheck.Status switch
        {
            UpdateStatus.Available => UiTheme.Accent,
            UpdateStatus.Failed => UiTheme.Danger,
            UpdateStatus.Current => UiTheme.StateOn,
            _ => UiTheme.TextMuted
        };

        var overflow = _textWidth + 4f - _viewport.rect.width;
        if (overflow <= 0f || _viewport.rect.width <= 0f)
        {
            _textRect.anchoredPosition = Vector2.zero;
            return;
        }

        const float pause = 2.5f;
        const float speed = 35f;
        var travelTime = overflow / speed;
        var cycle = 2f * (pause + travelTime);
        _elapsed = (_elapsed + Time.unscaledDeltaTime) % cycle;
        var offset = _elapsed < pause ? 0f
            : _elapsed < pause + travelTime ? (_elapsed - pause) * speed
            : _elapsed < 2f * pause + travelTime ? overflow
            : overflow - (_elapsed - 2f * pause - travelTime) * speed;
        _textRect.anchoredPosition = new Vector2(-offset, 0f);
    }
}
