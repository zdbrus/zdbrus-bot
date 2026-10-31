using System;
using System.Globalization;
using Firebot.UI.Config;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Firebot.UI.Widgets;

internal sealed class PreciseIntegerRow : UiRow
{
    public override float Height => 76f;

    public PreciseIntegerRow(ConfigEntry config, RectTransform rowRect) : base(config, rowRect)
    {
        var label = BuildLabel();
        label.rectTransform.anchorMin = new Vector2(0f, 1f);
        label.rectTransform.anchorMax = new Vector2(LabelRatio, 1f);
        label.rectTransform.offsetMin = new Vector2(UiTheme.RowInsetLeft, -UiTheme.RowHeight);
        label.rectTransform.offsetMax = new Vector2(-UiTheme.RowInsetRight, 0f);

        var background = UiFactory.CreateImage("input", RowRect, UiTheme.Button, true);
        var rect = background.rectTransform;
        rect.anchorMin = new Vector2(LabelRatio + 0.03f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(0f, -UiTheme.InputHeight - 7f);
        rect.offsetMax = new Vector2(-UiTheme.InputInsetRight, -7f);

        var viewport = UiFactory.CreateNode("textArea", rect);
        UiFactory.Stretch(viewport, UiTheme.InputTextInset);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = UiFactory.CreateLabel(viewport, config.ReadInt().ToString(CultureInfo.InvariantCulture),
            UiTheme.LabelFontSize, UiTheme.Text, TextAlignmentOptions.Left);
        UiFactory.Stretch(text.rectTransform);

        var input = background.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewport;
        input.textComponent = text;
        if (UiTheme.Font != null) input.fontAsset = UiTheme.Font;
        input.pointSize = UiTheme.LabelFontSize;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 10;
        input.SetTextWithoutNotify(config.ReadInt().ToString(CultureInfo.InvariantCulture));

        var hintText = config.Key.EndsWith("efficiency_percent", StringComparison.Ordinal)
            ? "0 disables this trigger" : "0 removes the minimum";
        var hint = UiFactory.CreateLabel(RowRect, hintText, UiTheme.ChipFontSize, UiTheme.TextMuted,
            TextAlignmentOptions.Left);
        hint.rectTransform.anchorMin = new Vector2(LabelRatio + 0.03f, 0f);
        hint.rectTransform.anchorMax = new Vector2(1f, 0f);
        hint.rectTransform.offsetMin = new Vector2(0f, 3f);
        hint.rectTransform.offsetMax = new Vector2(-UiTheme.InputInsetRight, 25f);

        input.onEndEdit.AddListener((Action<string>)(value =>
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 0)
            {
                config.Write(number);
                hint.text = hintText;
                hint.color = UiTheme.TextMuted;
                return;
            }

            hint.text = "Enter a whole number from 0 to 2147483647.";
            hint.color = UiTheme.Danger;
        }));
    }
}
