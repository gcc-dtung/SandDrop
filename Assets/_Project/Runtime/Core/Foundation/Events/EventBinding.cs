using System;
using GameKit.Core.Contracts;

namespace GameKit.Core.Foundation.Events
{
    /// <summary>
    /// Returned by <see cref="EventBus{T}.Subscribe(Action{T})"/>. Keep it in a field and dispose it to unsubscribe.
    /// </summary>
    public readonly struct EventBinding<T> : IDisposable where T : struct, IEvent
    {
        private readonly Action<T> handler;

        internal EventBinding(Action<T> handler)
        {
            this.handler = handler;
        }

        public void Dispose()
        {
            EventBus<T>.Unsubscribe(handler);
        }
    }
}
