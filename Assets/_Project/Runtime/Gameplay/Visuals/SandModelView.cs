using DG.Tweening;
using GameKit.Gameplay.Voxel;
using UnityEngine;

namespace GameKit.Gameplay.Visuals
{
    /// <summary>
    /// Pivot that holds the sand model at a fixed point on screen. This transform is the rotation / zoom pivot;
    /// the model is its child and slides so that the centre of the blocks still on the board sits on the pivot.
    /// Rotation is trackball style (world axes from the camera, no limits) with a light inertia after release.
    /// All movement runs through DOTween.
    /// </summary>
    public sealed class SandModelView : MonoBehaviour
    {
        [SerializeField] private SandModelRenderer model;

        [Header("Recentre / auto zoom")]
        [SerializeField] private float refitDuration = 0.35f;
        [Tooltip("Largest automatic zoom when few blocks are left (1 = no auto zoom).")]
        [SerializeField] private float maxAutoZoom = 1.6f;
        [Tooltip("Auto zoom starts only when what is left is smaller than this share of the start size (0.55 = 55%).")]
        [Range(0.1f, 1f)]
        [SerializeField] private float autoZoomBelow = 0.55f;
        [SerializeField] private Vector2 userZoomRange = new Vector2(0.5f, 2f);
        [SerializeField] private float maxTotalScale = 2.4f;

        [Header("Fit to screen")]
        [Tooltip("Camera that shows the model (Main Camera when empty).")]
        [SerializeField] private Camera viewCamera;
        [Tooltip("Viewport area (0-1) the model must stay inside: below the waiting row, inside the screen edges.")]
        [SerializeField] private Rect fitArea = new Rect(0.04f, 0.04f, 0.92f, 0.56f);

        [Header("Inertia")]
        [SerializeField] private float inertiaDuration = 0.45f;
        [Tooltip("Share of the release speed kept for the inertia spin (lower = lighter).")]
        [SerializeField] private float inertiaStrength = 0.6f;
        [SerializeField] private float maxInertiaSpeed = 540f;
        [SerializeField] private float minInertiaSpeed = 30f;

        [Header("Reset view")]
        [SerializeField] private float resetDuration = 0.35f;

        private Tween moveTween;
        private Tween fitTween;
        private Tween rotateTween;
        private Tween inertiaTween;

        private float startRadius = 1f;
        private float autoZoom = 1f;
        private readonly System.Collections.Generic.List<Vector3> outline = new System.Collections.Generic.List<Vector3>();
        /// <summary>Scale that makes the whole model fit <see cref="fitArea"/> at the start of the level (at most 1).</summary>
        private float fitZoom = 1f;
        private float userZoom = 1f;
        private Vector3 angularVelocity; // world axis * degrees per second
        private bool dragging;

        public SandModelRenderer Model => model;
        public float UserZoom => userZoom;

        /// <summary>New level: start rotation, zoom 1, model centred on all of its blocks.</summary>
        public void ResetForLevel(Quaternion rotation)
        {
            KillAll();
            transform.localRotation = rotation;
            userZoom = 1f;
            autoZoom = 1f;
            fitZoom = 1f;
            ApplyScale();
            angularVelocity = Vector3.zero;
            dragging = false;
            if (model == null)
            {
                return;
            }
            float radius;
            Vector3 center;
            startRadius = model.TryGetRemainingBounds(out center, out radius) ? Mathf.Max(0.01f, radius) : 1f;
            model.transform.localPosition = -center;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            fitZoom = Mathf.Min(1f, ScaleToFit());
            ApplyScale();
        }

        /// <summary>Slides the model so the blocks still on the board are centred, and zooms in as it shrinks.</summary>
        public void Refit()
        {
            if (model == null)
            {
                return;
            }
            Vector3 center;
            float radius;
            if (!model.TryGetRemainingBounds(out center, out radius))
            {
                return; // Nothing left: keep the view as it is for the end of the level.
            }

            Transform child = model.transform;
            Vector3 targetPosition = -center;
            moveTween?.Kill();
            if ((child.localPosition - targetPosition).sqrMagnitude < 1e-8f)
            {
                child.localPosition = targetPosition;
            }
            else
            {
                moveTween = child.DOLocalMove(targetPosition, refitDuration)
                    .SetEase(Ease.OutCubic)
                    .SetLink(child.gameObject);
            }

            // No zoom until what is left is clearly smaller than the start model; then zoom so it stays
            // at that threshold size (continuous, no jump when crossing it).
            float threshold = startRadius * autoZoomBelow;
            float targetZoom = Mathf.Clamp(threshold / Mathf.Max(0.01f, radius), 1f, Mathf.Max(1f, maxAutoZoom));
            // Never zoom past what still fits the play area (as seen from the current rotation).
            targetZoom = Mathf.Max(1f, Mathf.Min(targetZoom, ScaleToFit() / Mathf.Max(0.01f, fitZoom)));
            fitTween?.Kill();
            if (Mathf.Abs(autoZoom - targetZoom) < 1e-4f)
            {
                autoZoom = targetZoom;
                ApplyScale();
            }
            else
            {
                fitTween = DOTween.To(() => autoZoom, v =>
                    {
                        autoZoom = v;
                        ApplyScale();
                    }, targetZoom, refitDuration)
                    .SetEase(Ease.OutCubic)
                    .SetLink(gameObject);
            }
        }

        /// <summary>Zoom from the HUD slider, on top of the automatic zoom.</summary>
        public void SetUserZoom(float zoom)
        {
            userZoom = Mathf.Clamp(zoom, userZoomRange.x, userZoomRange.y);
            ApplyScale();
        }

        /// <summary>Back to the level's start rotation and user zoom 1 (the model stays centred on what is left).</summary>
        public void ResetView(Quaternion rotation)
        {
            StopSpin();
            rotateTween?.Kill();
            if (Quaternion.Angle(transform.localRotation, rotation) < 0.01f)
            {
                transform.localRotation = rotation;
            }
            else
            {
                rotateTween = transform.DOLocalRotateQuaternion(rotation, resetDuration)
                    .SetEase(Ease.OutCubic)
                    .SetLink(gameObject);
            }

            float from = userZoom;
            fitTween?.Kill();
            // Re-run the auto zoom target as part of the reset so both values settle together.
            if (Mathf.Abs(from - 1f) < 1e-4f)
            {
                userZoom = 1f;
                ApplyScale();
                Refit();
            }
            else
            {
                fitTween = DOTween.To(() => userZoom, v =>
                    {
                        userZoom = v;
                        ApplyScale();
                    }, 1f, resetDuration)
                    .SetEase(Ease.OutCubic)
                    .SetLink(gameObject)
                    .OnComplete(Refit);
            }
        }

        public void BeginDrag()
        {
            StopSpin();
            rotateTween?.Kill(); // The finger takes over from a running reset.
            dragging = true;
            angularVelocity = Vector3.zero;
        }

        /// <summary>Rotates by <paramref name="degrees"/> around a world axis during a drag.</summary>
        public void DragBy(Vector3 worldAxis, float degrees, float deltaTime)
        {
            if (!dragging)
            {
                BeginDrag();
            }
            if (degrees != 0f && worldAxis.sqrMagnitude > 1e-8f)
            {
                transform.Rotate(worldAxis.normalized, degrees, Space.World);
            }

            if (deltaTime > 0f)
            {
                Vector3 instant = worldAxis.sqrMagnitude > 1e-8f ? worldAxis.normalized * (degrees / deltaTime) : Vector3.zero;
                angularVelocity = Vector3.Lerp(angularVelocity, instant, 0.5f);
            }
        }

        /// <summary>Finger lifted: keep spinning a little, slowing down.</summary>
        public void EndDrag()
        {
            dragging = false;
            float speed = angularVelocity.magnitude * inertiaStrength;
            if (speed < minInertiaSpeed)
            {
                angularVelocity = Vector3.zero;
                return;
            }

            Vector3 axis = angularVelocity.normalized;
            speed = Mathf.Min(speed, maxInertiaSpeed);
            angularVelocity = Vector3.zero;
            inertiaTween?.Kill();
            float factor = 1f;
            inertiaTween = DOTween.To(() => factor, v => factor = v, 0f, inertiaDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject)
                .OnUpdate(() => transform.Rotate(axis, speed * factor * Time.deltaTime, Space.World));
        }

        public void StopSpin()
        {
            inertiaTween?.Kill();
            inertiaTween = null;
        }

        /// <summary>
        /// Scale (of this pivot) at which the blocks still on the board just fit <see cref="fitArea"/>, seen from the
        /// current rotation: the outline cells are projected on the camera axes with perspective, each side of the
        /// pivot checked on its own. 1 when it cannot be measured.
        /// </summary>
        private float ScaleToFit()
        {
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null || model == null || !model.TryGetRemainingBox(out Vector3 center, out _) ||
                !model.GetRemainingOutline(outline))
            {
                return 1f;
            }

            Vector3 pivot = transform.position;
            Vector3 viewport = cam.WorldToViewportPoint(pivot);
            float distance = viewport.z;
            if (distance <= 0.01f)
            {
                return 1f;
            }
            // World size of the view at the pivot's depth.
            float halfHeight = cam.orthographic ? cam.orthographicSize : distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * cam.aspect;
            float up = (fitArea.yMax - viewport.y) * 2f * halfHeight;
            float down = (viewport.y - fitArea.yMin) * 2f * halfHeight;
            float right = (fitArea.xMax - viewport.x) * 2f * halfWidth;
            float left = (viewport.x - fitArea.xMin) * 2f * halfWidth;

            // The model is (or is sliding to be) centred on the pivot; the child has no rotation of its own.
            // Camera axes are turned into the model's space once, so each point only needs three dot products.
            Quaternion toModel = Quaternion.Inverse(transform.rotation);
            Transform camT = cam.transform;
            Vector3 rightAxis = toModel * camT.right, upAxis = toModel * camT.up, forwardAxis = toModel * camT.forward;
            bool perspective = !cam.orthographic;
            float minDistance = 0.1f * distance;
            float pad = model.GrainSize * 0.5f;
            float maxUp = 0f, maxDown = 0f, maxRight = 0f, maxLeft = 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector3 v = outline[i] - center;
                float x = Vector3.Dot(v, rightAxis);
                float y = Vector3.Dot(v, upAxis);
                if (perspective)
                {
                    // Closer points look bigger; measured at scale 1, slightly conservative when the model shrinks.
                    float k = distance / Mathf.Max(minDistance, distance + Vector3.Dot(v, forwardAxis));
                    x *= k;
                    y *= k;
                }
                if (x + pad > maxRight) maxRight = x + pad;
                if (pad - x > maxLeft) maxLeft = pad - x;
                if (y + pad > maxUp) maxUp = y + pad;
                if (pad - y > maxDown) maxDown = pad - y;
            }

            float scale = float.MaxValue;
            scale = Limit(scale, up, maxUp);
            scale = Limit(scale, down, maxDown);
            scale = Limit(scale, right, maxRight);
            scale = Limit(scale, left, maxLeft);
            return scale == float.MaxValue ? 1f : Mathf.Max(0.05f, scale);
        }

        private static float Limit(float scale, float room, float extent)
        {
            return extent > 1e-4f && room > 0f ? Mathf.Min(scale, room / extent) : scale;
        }

        private void ApplyScale()
        {
            float scale = Mathf.Min(userZoom * autoZoom * fitZoom, maxTotalScale);
            transform.localScale = Vector3.one * scale;
        }

        private void KillAll()
        {
            moveTween?.Kill();
            fitTween?.Kill();
            rotateTween?.Kill();
            inertiaTween?.Kill();
            moveTween = fitTween = rotateTween = inertiaTween = null;
        }

        private void OnDisable()
        {
            KillAll();
        }

        private void OnDestroy()
        {
            KillAll();
        }
    }
}
