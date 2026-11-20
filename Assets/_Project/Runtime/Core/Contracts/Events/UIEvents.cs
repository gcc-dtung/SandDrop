using System;

namespace GameKit.Core.Contracts
{
    /// <summary>Raised when a popup has finished opening (e.g. pause gameplay input here).</summary>
    public struct PopupOpened : IEvent
    {
        public Type PopupType;
    }

    /// <summary>Raised when a popup has finished closing.</summary>
    public struct PopupClosed : IEvent
    {
        public Type PopupType;
    }
}
