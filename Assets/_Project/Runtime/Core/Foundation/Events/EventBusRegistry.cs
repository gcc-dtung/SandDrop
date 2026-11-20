using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameKit.Core.Foundation.Events
{
    /// <summary>
    /// Keeps track of every <see cref="EventBus{T}"/> that has been used, so they can be listed (Event Bus Monitor)
    /// and cleared together.
    /// </summary>
    public static class EventBusRegistry
    {
        public readonly struct Entry
        {
            public readonly Type EventType;
            private readonly Func<int> listenerCount;
            private readonly Func<int> raiseCount;
            private readonly Action clear;

            internal Entry(Type eventType, Func<int> listenerCount, Func<int> raiseCount, Action clear)
            {
                EventType = eventType;
                this.listenerCount = listenerCount;
                this.raiseCount = raiseCount;
                this.clear = clear;
            }

            public int ListenerCount => listenerCount();
            public int RaiseCount => raiseCount();
            public void Clear() => clear();
        }

        private static readonly List<Entry> entries = new List<Entry>();

        public static IReadOnlyList<Entry> Entries => entries;

        internal static void Register(Type eventType, Func<int> listenerCount, Func<int> raiseCount, Action clear)
        {
            entries.Add(new Entry(eventType, listenerCount, raiseCount, clear));
        }

        public static void ClearAll()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].Clear();
            }
        }

        // With Domain Reload disabled, static listeners would survive into the next Play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode()
        {
            ClearAll();
        }
    }
}
