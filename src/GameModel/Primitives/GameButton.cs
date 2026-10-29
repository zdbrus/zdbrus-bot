using System;
using System.Collections;
using Firebot.GameModel.Base;
using UnityEngine;
using UnityEngine.UI;
using static Firebot.Core.BotSettings;

namespace Firebot.GameModel.Primitives;

public class GameButton : GameElement
{
    public GameButton(string path = null, GameElement parent = null, Transform transform = null) :
        base(path, parent, transform) { }

    public bool IsClickable() => IsClickable(out _);

    private bool IsClickable(out Button button)
    {
        button = null;
        if (!IsVisible()) return false;

        if (!TryGetComponent(out button)) return false;
        return button.enabled && button.interactable;
    }

    public virtual IEnumerator Click()
    {
        if (IsClickable(out var button))
            try
            {
                button.onClick.Invoke();
            }
            catch (Exception e)
            {
                Debug($"[FAILED] Click threw exception: {e.Message}. Path: {Path}");
            }
        else
        {
            if (button != null)
                Debug($"[FAILED] Click ignored: Button disabled/non-interactable. Path: {Path}");
        }

        yield return new WaitForSeconds(InteractionDelay);
    }

    public IEnumerator ClickUntil(Func<bool> stateChanged, Action<bool> result, string label, float timeout = 2f)
    {
        if (!IsClickable())
        {
            Debug($"[FAILED] {label}: button is not clickable. Path: {Path}");
            result(false);
            yield break;
        }

        yield return Click();
        var deadline = Time.unscaledTime + timeout;
        while (!stateChanged() && Time.unscaledTime < deadline) yield return null;

        var changed = stateChanged();
        if (!changed)
            Debug($"[FAILED] {label}: clicked, but no confirming game-state change was observed.");
        result(changed);
    }
}