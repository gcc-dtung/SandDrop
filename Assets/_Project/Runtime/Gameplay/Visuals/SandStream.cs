using UnityEngine;

namespace GameKit.Gameplay.Visuals
{
    /// <summary>
    /// Handle for sand launched by <see cref="SandFlight.LaunchHeld"/>: the grains fly to a hover point and swirl
    /// there until <see cref="Release"/> gives them their jar.
    /// </summary>
    public sealed class SandStream
    {
        internal float ReleaseTime = float.MaxValue;
        internal Vector3 Target;

        /// <summary>Time (Time.time) when the last grain reaches the hover point.</summary>
        internal float LastHoverArrival;

        /// <summary>Time (Time.time) when the first grain reaches the hover point.</summary>
        internal float FirstHoverArrival;

        public bool Released => ReleaseTime < float.MaxValue;

        /// <summary>Sends the hovering sand (and any still on its way) into <paramref name="target"/>.</summary>
        public void Release(Vector3 target)
        {
            Target = target;
            ReleaseTime = Time.time;
        }
    }
}
