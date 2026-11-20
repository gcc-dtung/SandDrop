namespace GameKit.Core.Contracts
{
    /// <summary>
    /// Optional callbacks for components on a pooled object.
    /// In <see cref="OnDespawned"/>: kill tweens, stop particles, reset scale / colour / alpha so the next Get starts clean.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>Called after the object is taken from the pool and activated.</summary>
        void OnSpawned();

        /// <summary>Called before the object is deactivated and returned to the pool.</summary>
        void OnDespawned();
    }
}
