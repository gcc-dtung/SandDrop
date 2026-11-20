namespace GameKit.Core.Contracts
{
    /// <summary>
    /// Marker for events sent through <c>EventBus&lt;T&gt;</c>.
    /// Implement it on a <c>struct</c> so raising an event never allocates.
    /// </summary>
    public interface IEvent
    {
    }
}
