using GameKit.Core.Foundation.Logging;
using UnityEngine;

namespace GameKit.Gameplay
{
    /// <summary>Rotates the transform at a configurable speed around an axis.</summary>
    public sealed class Rotator : MonoBehaviour
    {
        private const float MinAxisSqrMagnitude = 1e-6f;

        [SerializeField] private Vector3 axis = Vector3.up;
        [SerializeField] private float degreesPerSecond = 90f;
        [SerializeField] private Space space = Space.Self;

        private bool warnedZeroAxis;

        /// <summary>Degrees per second; negative rotates the other way.</summary>
        public float DegreesPerSecond
        {
            get => degreesPerSecond;
            set => degreesPerSecond = value;
        }

        /// <summary>Rotation axis (normalized when used).</summary>
        public Vector3 Axis
        {
            get => axis;
            set => axis = value;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        /// <summary>Rotates by degreesPerSecond * deltaTime around the axis in the configured space.</summary>
        public void Step(float deltaTime)
        {
            if (axis.sqrMagnitude < MinAxisSqrMagnitude)
            {
                if (!warnedZeroAxis)
                {
                    warnedZeroAxis = true;
                    Log.Warning("Rotator", "Axis is zero, not rotating.", this);
                }
                return;
            }

            warnedZeroAxis = false;
            transform.Rotate(axis.normalized, degreesPerSecond * deltaTime, space);
        }
    }
}
