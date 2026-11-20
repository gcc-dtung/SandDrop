using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameKit.Core.Contracts;
using UnityEngine;
using GameKit.Core.Foundation.Logging;

namespace GameKit.Core.Foundation.Events
{
    /// <summary>
    /// Static, type-safe event bus: one bus per event struct.
    /// <code>
    /// EventBus&lt;LevelCompleted&gt;.Subscribe(OnLevelCompleted, this.GetCancellationTokenOnDestroy());
    /// EventBus&lt;LevelCompleted&gt;.Raise(new LevelCompleted { Level = 3 });
    /// var e = await EventBus&lt;LevelCompleted&gt;.WaitNext(token);
    /// </code>
    /// Raise does not allocate. Subscribing or unsubscribing while an event is being raised is safe:
    /// the change applies from the next Raise, so a listener removed mid-Raise still receives the current event.
    /// A listener that throws is logged and does not stop the others.
    /// </summary>
    public static class EventBus<T> where T : struct, IEvent
    {
        private static readonly List<Action<T>> handlers = new List<Action<T>>();
        private static Action<T>[] snapshot = Array.Empty<Action<T>>();
        private static bool dirty;
        private static int raiseCount;

        static EventBus()
        {
            EventBusRegistry.Register(typeof(T), () => handlers.Count, () => raiseCount, Clear);
        }

        /// <summary>Number of listeners currently subscribed.</summary>
        public static int ListenerCount => handlers.Count;

        /// <summary>Subscribe until the returned binding is disposed (or <see cref="Unsubscribe"/> is called).</summary>
        public static EventBinding<T> Subscribe(Action<T> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (handlers.Contains(handler))
            {
                Log.Warning("EventBus", $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name} is already subscribed to {typeof(T).Name}; ignoring the duplicate.");
                return new EventBinding<T>(handler);
            }

            handlers.Add(handler);
            dirty = true;
            return new EventBinding<T>(handler);
        }

        /// <summary>
        /// Subscribe until <paramref name="token"/> is cancelled. For a MonoBehaviour pass
        /// <c>this.GetCancellationTokenOnDestroy()</c> so the listener is removed when the object is destroyed.
        /// </summary>
        public static void Subscribe(Action<T> handler, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            Subscribe(handler);
            token.Register(() => Unsubscribe(handler));
        }

        public static void Unsubscribe(Action<T> handler)
        {
            if (handler != null && handlers.Remove(handler))
            {
                dirty = true;
            }
        }

        public static void Raise(T evt)
        {
            if (dirty)
            {
                snapshot = handlers.ToArray();
                dirty = false;
            }

            raiseCount++;

            // Local copy: a nested Raise or a subscription change inside a handler swaps the field, not this array.
            var current = snapshot;
            for (int i = 0; i < current.Length; i++)
            {
                try
                {
                    current[i](evt);
                }
                catch (Exception e)
                {
                    Log.Exception(e);
                }
            }
        }

        /// <summary>Wait for the next time this event is raised.</summary>
        public static async UniTask<T> WaitNext(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            var source = new UniTaskCompletionSource<T>();
            Action<T> handler = e => source.TrySetResult(e);
            Subscribe(handler);
            try
            {
                using (token.Register(() => source.TrySetCanceled(token)))
                {
                    return await source.Task;
                }
            }
            finally
            {
                Unsubscribe(handler);
            }
        }

        /// <summary>Remove every listener. Called automatically when entering Play mode.</summary>
        public static void Clear()
        {
            handlers.Clear();
            snapshot = Array.Empty<Action<T>>();
            dirty = false;
            raiseCount = 0;
        }
    }
}
