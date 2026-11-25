#if MONO
using System;
using UnityEngine.Events;

namespace Firebot;

/// <summary>
///     On IL2CPP, Il2CppInterop converts <see cref="Action" /> to <see cref="UnityAction" /> implicitly, so
///     listeners are added straight from C# delegates. Mono has no such conversion; these overloads are
///     picked only when the instance method does not apply, which keeps the call sites identical.
/// </summary>
public static class UnityEventExtensions
{
    public static void AddListener(this UnityEvent unityEvent, Action action) =>
        unityEvent.AddListener(new UnityAction(action));

    public static void AddListener<T>(this UnityEvent<T> unityEvent, Action<T> action) =>
        unityEvent.AddListener(new UnityAction<T>(action));
}
#endif
