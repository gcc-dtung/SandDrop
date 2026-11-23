using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace GameKit.Gameplay.Visuals
{
    /// <summary>
    /// A glass jar that fills with coloured sand. The sand mesh has its origin at the jar bottom, so scaling it
    /// on Y shows how full the jar is. A cork lid pops on when the tray is complete.
    /// </summary>
    public sealed class JarView : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Transform sand;
        [SerializeField] private Renderer sandRenderer;
        [SerializeField] private Transform lid;
        [SerializeField] private Renderer lidRenderer;
        [SerializeField] private Color lidColor = new Color(0.72f, 0.52f, 0.34f);
        [Tooltip("Local point where flying sand enters the jar.")]
        [SerializeField] private Vector3 mouth = new Vector3(0f, 0.3f, 0f);

        private MaterialPropertyBlock block;
        private Tween fillTween;
        private Tween lidTween;
        private float fill;
        private Vector3 lidScale = Vector3.one;
        private bool lidScaleKnown;

        public float Fill => fill;
        public Vector3 MouthPosition => transform.TransformPoint(mouth);

        public void SetColor(Color color)
        {
            block ??= new MaterialPropertyBlock();
            block.SetColor(ColorId, color);
            if (sandRenderer != null)
            {
                sandRenderer.SetPropertyBlock(block);
            }
            if (lidRenderer != null)
            {
                block.SetColor(ColorId, lidColor);
                lidRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Empty jar, no lid, no running tweens.</summary>
        public void ResetJar()
        {
            fillTween?.Kill();
            lidTween?.Kill();
            SetFill(0f);
            RememberLid();
            if (lid != null)
            {
                lid.localScale = lidScale;
                lid.gameObject.SetActive(false);
            }
        }

        /// <summary>Sand level goes from its current value to <paramref name="target"/> (0..1).</summary>
        public UniTask FillToAsync(float target, float delay, float duration, CancellationToken token)
        {
            fillTween?.Kill();
            fillTween = DOTween.To(() => fill, SetFill, target, duration)
                .SetDelay(delay)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
            return fillTween.ToUniTask(cancellationToken: token);
        }

        public UniTask PutLidAsync(CancellationToken token)
        {
            RememberLid();
            if (lid == null)
            {
                return UniTask.CompletedTask;
            }
            lidTween?.Kill();
            lid.gameObject.SetActive(true);
            lid.localScale = Vector3.zero;
            lidTween = lid.DOScale(lidScale, 0.18f).SetEase(Ease.OutBack).SetLink(lid.gameObject);
            return lidTween.ToUniTask(cancellationToken: token);
        }

        private void SetFill(float value)
        {
            fill = Mathf.Clamp01(value);
            if (sand != null)
            {
                sand.localScale = new Vector3(1f, Mathf.Max(0.0001f, fill), 1f);
                sand.gameObject.SetActive(fill > 0.001f);
            }
        }

        private void RememberLid()
        {
            if (!lidScaleKnown && lid != null)
            {
                lidScale = lid.localScale;
                lidScaleKnown = true;
            }
        }

        private void OnDisable()
        {
            fillTween?.Kill();
            lidTween?.Kill();
        }
    }
}
