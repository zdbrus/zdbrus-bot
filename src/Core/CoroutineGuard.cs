using System;
using System.Collections;
using System.Collections.Generic;

namespace Firebot.Core;

/// <summary>
///     Drives a coroutine so that a failing step stops the feature instead of ending the loop behind the mod's
///     back.
///     <para>
///         AutoSkill and AutoUpgrade start their loops straight from MelonCoroutines. An exception escaping one of
///         their steps is caught by Unity's scheduler, which logs a stack trace and kills the coroutine while the
///         mod's own flags still say the feature is running: the badge claims "on" with nothing behind it, and the
///         hotkey needs two presses to recover.
///     </para>
///     Nested iterators are driven here too, so exceptions in a click or hold reach the failure callback.
///     Unity wait instructions still pass through to the scheduler unchanged.
/// </summary>
public static class CoroutineGuard
{
    /// <summary>
    ///     Runs <paramref name="routine" />, and on a failing step reports it with <paramref name="context" /> and
    ///     calls <paramref name="onFailure" /> before ending.
    /// </summary>
    public static IEnumerator Run(IEnumerator routine, string context, Action onFailure = null)
    {
        var failed = false;
        void Report(string message)
        {
            Logger.Error($"[FAILED] {context}: {message}");
            if (failed) return;
            failed = true;
            onFailure?.Invoke();
        }

        if (routine == null)
        {
            Report("returned null routine.");
            yield break;
        }

        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        try
        {
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                bool movedNext;
                object yielded;
                try
                {
                    movedNext = current.MoveNext();
                    yielded = movedNext ? current.Current : null;
                    if (!movedNext)
                    {
                        stack.Pop();
                        (current as IDisposable)?.Dispose();
                    }
                    else if (yielded is IEnumerator child)
                    {
                        if (stack.Contains(child))
                            throw new InvalidOperationException("A coroutine yielded an active parent iterator.");
                        stack.Push(child);
                    }
                }
                catch (Exception e)
                {
                    Report($"threw: {e.GetType().Name} - {e.Message}");
                    yield break;
                }

                if (movedNext && yielded is not IEnumerator) yield return yielded;
            }
        }
        finally
        {
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                try { (current as IDisposable)?.Dispose(); }
                catch (Exception e) { Report($"cleanup threw: {e.GetType().Name} - {e.Message}"); }
            }
        }
    }
}
