using System;
using GameKit.Gameplay.Visuals;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameKit.Gameplay.Input
{
    /// <summary>
    /// Old Input Manager pointer handling for the sand model: a short press is a tap, dragging rotates the model
    /// trackball style (around the camera's up / right axes, no limits, the surface follows the finger).
    /// Touches simulate the mouse.
    /// </summary>
    public sealed class SandInput : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private SandModelView view;
        [Tooltip("Pointer movement (in inches) before a press counts as a drag.")]
        [SerializeField] private float dragThresholdInches = 0.08f;
        [Tooltip("Degrees of rotation for a drag across the full screen height.")]
        [SerializeField] private float degreesPerScreenHeight = 320f;
        [Tooltip("Seconds a still press must last to count as a hold (see inside) instead of a tap.")]
        [SerializeField] private float holdSeconds = 0.3f;

        private bool pressed;
        private bool holding;
        private float pressTime;
        private bool dragging;
        private bool pressStartedOverUi;
        private Vector2 pressPosition;
        private Vector2 lastPosition;
        private bool pressSignalled;

        /// <summary>Finger down on the play area (screen position): touch feedback starts here.</summary>
        public event Action<Vector2> PressDown;

        /// <summary>
        /// The press stops being a press: the finger lifts (raised before <see cref="Tapped"/>) or starts dragging.
        /// </summary>
        public event Action PressUp;

        /// <summary>Raised with the screen position of a tap.</summary>
        public event Action<Vector2> Tapped;

        /// <summary>Press held still for <c>holdSeconds</c> (screen position): show the inside. A hold never becomes a tap.</summary>
        public event Action<Vector2> HoldStarted;

        /// <summary>The finger of a hold is lifted.</summary>
        public event Action HoldEnded;

        /// <summary>Taps are ignored while true (rotation still works).</summary>
        public bool TapsLocked { get; set; }

        public Camera ViewCamera => viewCamera;

        private void OnValidate()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main; // Editor-time fill only; never looked up at runtime.
            }
        }

        private void Update()
        {
            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                pressed = true;
                dragging = false;
                holding = false;
                pressTime = Time.unscaledTime;
                pressStartedOverUi = IsPointerOverUi();
                pressPosition = lastPosition = UnityEngine.Input.mousePosition;
                pressSignalled = !pressStartedOverUi;
                if (pressSignalled)
                {
                    PressDown?.Invoke(pressPosition);
                }
            }

            if (!pressed)
            {
                return;
            }

            Vector2 position = UnityEngine.Input.mousePosition;
            if (UnityEngine.Input.GetMouseButton(0))
            {
                if (!dragging && !pressStartedOverUi && (position - pressPosition).magnitude > DragThresholdPixels())
                {
                    dragging = true;
                    EndPress();
                    if (view != null)
                    {
                        view.BeginDrag();
                    }
                }
                if (!dragging && !holding && !pressStartedOverUi && Time.unscaledTime - pressTime >= holdSeconds)
                {
                    holding = true; // Dragging after this still rotates, with the inside shown.
                    HoldStarted?.Invoke(position);
                }
                if (dragging)
                {
                    Rotate(position - lastPosition);
                }
                lastPosition = position;
            }

            if (UnityEngine.Input.GetMouseButtonUp(0))
            {
                EndPress();
                if (!dragging && !holding && !pressStartedOverUi && !TapsLocked)
                {
                    Tapped?.Invoke(position);
                }
                if (holding)
                {
                    HoldEnded?.Invoke();
                }
                holding = false;
                if (dragging && view != null)
                {
                    view.EndDrag();
                }
                pressed = false;
                dragging = false;
            }
        }

        private void EndPress()
        {
            if (!pressSignalled)
            {
                return;
            }
            pressSignalled = false;
            PressUp?.Invoke();
        }

        private void Rotate(Vector2 delta)
        {
            if (view == null)
            {
                return;
            }

            // Trackball: drag right turns the front surface right (around camera up), drag up turns it up
            // (around camera right). Axes come from the camera, so it feels the same at any orientation.
            Camera cam = ViewCamera;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            float degrees = delta.magnitude / Mathf.Max(1f, Screen.height) * degreesPerScreenHeight;
            Vector3 axis = up * -delta.x + right * delta.y;
            view.DragBy(axis, degrees, Time.unscaledDeltaTime);
        }

        private float DragThresholdPixels()
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            return dragThresholdInches * dpi;
        }

        private static bool IsPointerOverUi()
        {
            EventSystem system = EventSystem.current;
            if (system == null)
            {
                return false;
            }
            if (UnityEngine.Input.touchCount > 0)
            {
                return system.IsPointerOverGameObject(UnityEngine.Input.GetTouch(0).fingerId);
            }
            return system.IsPointerOverGameObject();
        }
    }
}
