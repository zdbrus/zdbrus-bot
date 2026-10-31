using System;
using System.Collections;
using Firebot.GameModel.Base;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Firebot.GameModel.Primitives;

public class CachedGameButton : GameButton
{
    private static readonly System.Collections.Generic.HashSet<string> LoggedMissing = new();

    private Transform _cachedRoot;
    private GameObject _heldTarget;
    private PointerEventData _heldData;
    private bool _holdCancelled;

    public CachedGameButton(string path = null, GameElement parent = null, Transform transform = null)
        : base(path, parent, transform) { }

    private Transform CachedRoot
    {
        get
        {
            if (_cachedRoot != null) return _cachedRoot;

            _cachedRoot = ResolvePath(Path);

            if (_cachedRoot == null && !string.IsNullOrEmpty(Path))
                if (LoggedMissing.Add(Path)) Debug($"[FAILED] Critical: Could not resolve Transform. Path: {Path}");

            return _cachedRoot;
        }
    }

    private Transform ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var slashIndex = path.IndexOf('/');
        if (slashIndex == -1)
            return GameObject.Find(path)?.transform;

        var rootName = path[..slashIndex];
        var rootObj = GameObject.Find(rootName);
        if (rootObj == null) return null;

        var relativePath = path[(slashIndex + 1)..];
        return rootObj.transform.Find(relativePath);
    }

    private bool IsVisibleCached()
    {
        var currentRoot = CachedRoot;
        return currentRoot != null && currentRoot.gameObject.activeInHierarchy;
    }

    private bool TryGetCachedComponent<T>(out T component) where T : Component
    {
        component = null;

        var currentRoot = CachedRoot;
        return currentRoot != null && currentRoot.TryGetComponent(out component);
    }

    private bool IsClickableCached(out Button button)
    {
        button = null;
        if (!IsVisibleCached()) return false;

        if (!TryGetCachedComponent(out button)) return false;
        return button.enabled && button.interactable;
    }

    public IEnumerator Click(float interactionDelay = 0.5f)
    {
        if (IsClickableCached(out var button))
            try
            {
                button.onClick.Invoke();
            }
            catch (Exception e)
            {
                Debug($"[FAILED] Click threw exception: {e.Message}. Path: {Path}");
            }
        else if (button != null)
            Debug($"[FAILED] Click ignored: Button disabled/non-interactable. Path: {Path}");

        yield return new WaitForSeconds(interactionDelay);
    }

    public IEnumerator HoldButton(float maxSeconds = 3f)
    {
        if (!IsClickableCached(out var button))
            yield break;

        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            Debug($"[FAILED] HoldButton requires EventSystem.current. Path: {Path}");
            yield break;
        }

        var eventData = new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left };
        var target = button.gameObject;
        _heldTarget = target;
        _heldData = eventData;
        _holdCancelled = false;

        try
        {
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerDownHandler);
            var startTime = Time.unscaledTime;
            while (!_holdCancelled && button != null && target != null && target.activeInHierarchy &&
                   button.enabled && button.interactable && Time.unscaledTime - startTime < maxSeconds)
                yield return null;
        }
        finally
        {
            ReleaseHold();
        }

        if (!_holdCancelled && button != null && target != null && button.enabled &&
            button.interactable && target.activeInHierarchy)
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerClickHandler);
    }

    public void CancelHold()
    {
        _holdCancelled = true;
        ReleaseHold();
    }

    private void ReleaseHold()
    {
        var target = _heldTarget;
        var data = _heldData;
        _heldTarget = null;
        _heldData = null;
        if (target != null && data != null) ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
    }
}