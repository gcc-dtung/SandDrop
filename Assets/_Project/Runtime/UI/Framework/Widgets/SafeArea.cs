using UnityEngine;

namespace GameKit.UI.Framework
{
    /// <summary>
    /// Fits this RectTransform inside <see cref="Screen.safeArea"/> (notch, rounded corners, home bar).
    /// Put it on a full-screen child of a Canvas and place the UI that must stay visible under it;
    /// backgrounds stay outside so they still cover the whole screen.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        [SerializeField] private bool conformX = true;
        [SerializeField] private bool conformY = true;
        [SerializeField, HideInInspector] private RectTransform rect;

        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        private void OnEnable()
        {
            Apply();
        }

        // Cheap comparison each frame: safe area changes on rotation, split screen and in the Device Simulator.
        private void Update()
        {
            if (Screen.safeArea != lastSafeArea || Screen.width != lastScreenSize.x || Screen.height != lastScreenSize.y)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (rect == null) rect = (RectTransform)transform;

            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            if (lastScreenSize.x <= 0 || lastScreenSize.y <= 0)
            {
                return;
            }

            ComputeAnchors(lastSafeArea, lastScreenSize, conformX, conformY, out var min, out var max);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Anchors (0..1) that cover <paramref name="safeArea"/> on a screen of <paramref name="screenSize"/> pixels.</summary>
        public static void ComputeAnchors(Rect safeArea, Vector2 screenSize, bool conformX, bool conformY, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.one;
            if (screenSize.x <= 0f || screenSize.y <= 0f)
            {
                return;
            }

            if (conformX)
            {
                min.x = Mathf.Clamp01(safeArea.xMin / screenSize.x);
                max.x = Mathf.Clamp01(safeArea.xMax / screenSize.x);
            }
            if (conformY)
            {
                min.y = Mathf.Clamp01(safeArea.yMin / screenSize.y);
                max.y = Mathf.Clamp01(safeArea.yMax / screenSize.y);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (rect == null) rect = (RectTransform)transform;
        }
#endif
    }
}
