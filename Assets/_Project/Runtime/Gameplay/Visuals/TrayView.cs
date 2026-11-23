using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameKit.Core.Contracts;
using UnityEngine;

namespace GameKit.Gameplay.Visuals
{
    /// <summary>
    /// One tray position: a rounded tray in the tray colour holding 3 glass jars that fill with sand.
    /// States: open (colour), locked (grey with "+ Unlock", tap to open), empty (no trays left here: hidden).
    /// </summary>
    public sealed class TrayView : MonoBehaviour, IPoolable
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer body;
        [SerializeField] private Renderer floor;
        [Tooltip("Jars left to right.")]
        [SerializeField] private JarView[] jars = new JarView[3];
        [Tooltip("Shown while the tray is locked (+ badge and Unlock label).")]
        [SerializeField] private GameObject lockMark;
        [SerializeField] private Color lockedColor = new Color(0.55f, 0.58f, 0.64f);
        [Tooltip("How much lighter the tray floor is than the tray colour.")]
        [SerializeField, Range(0f, 1f)] private float floorLighten = 0.55f;
        [Tooltip("How much lighter the tray body is than the sand colour.")]
        [SerializeField, Range(0f, 1f)] private float bodyLighten = 0.15f;
        [SerializeField] private float popDuration = 0.25f;

        private MaterialPropertyBlock block;
        private Tween tween;
        private Color color;
        private Vector3 baseScale = Vector3.one;
        private bool baseScaleKnown;

        /// <summary>Jars already given to arriving sand (including the one being filled).</summary>
        public int Count { get; private set; }
        public bool Locked { get; private set; }

        /// <summary>Next empty jar for arriving sand, or null when all 3 are taken.</summary>
        public JarView ReserveJar()
        {
            if (Count >= jars.Length)
            {
                return null;
            }
            return jars[Count++];
        }

        /// <summary>Instant setup (level start).</summary>
        public void Show(int colorIndexOrMinus1, Color sandColor, bool locked)
        {
            KillTween();
            RememberScale();
            Locked = locked;
            color = sandColor;
            gameObject.SetActive(locked || colorIndexOrMinus1 >= 0);
            transform.localScale = baseScale;
            ResetJars();
            Paint();
        }

        /// <summary>Full tray: lids pop on one by one, a small bounce, then it shrinks away.</summary>
        public async UniTask PlayCompleteAsync(CancellationToken token)
        {
            KillTween();
            RememberScale();
            for (int i = 0; i < jars.Length; i++)
            {
                if (jars[i] != null)
                {
                    jars[i].PutLidAsync(token).Forget();
                    await UniTask.Delay(70, cancellationToken: token);
                }
            }
            await UniTask.Delay(150, cancellationToken: token);

            Sequence seq = DOTween.Sequence()
                .Append(transform.DOScale(baseScale * 1.12f, popDuration * 0.5f).SetEase(Ease.OutQuad))
                .Append(transform.DOScale(Vector3.zero, popDuration).SetEase(Ease.InBack))
                .SetLink(gameObject);
            tween = seq;
            await seq.ToUniTask(cancellationToken: token);
        }

        /// <summary>New tray at this position (colour -1 = none left: stays hidden).</summary>
        public async UniTask PlayAssignAsync(int colorIndexOrMinus1, Color sandColor, CancellationToken token)
        {
            KillTween();
            RememberScale();
            Locked = false;
            color = sandColor;
            ResetJars();
            if (colorIndexOrMinus1 < 0)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            Paint();
            transform.localScale = Vector3.zero;
            tween = transform.DOScale(baseScale, popDuration).SetEase(Ease.OutBack).SetLink(gameObject);
            await tween.ToUniTask(cancellationToken: token);
        }

        private void ResetJars()
        {
            Count = 0;
            foreach (JarView jar in jars)
            {
                if (jar != null)
                {
                    jar.ResetJar();
                }
            }
        }

        private void Paint()
        {
            block ??= new MaterialPropertyBlock();
            Color bodyColor = Locked ? lockedColor : Color.Lerp(color, Color.white, bodyLighten);
            SetColor(body, bodyColor);
            SetColor(floor, Color.Lerp(bodyColor, Color.white, floorLighten));
            foreach (JarView jar in jars)
            {
                if (jar == null)
                {
                    continue;
                }
                jar.gameObject.SetActive(!Locked);
                jar.SetColor(color);
            }
            if (lockMark != null)
            {
                lockMark.SetActive(Locked);
            }
        }

        private void SetColor(Renderer target, Color c)
        {
            if (target == null)
            {
                return;
            }
            block.SetColor(ColorId, c);
            target.SetPropertyBlock(block);
        }

        private void RememberScale()
        {
            if (!baseScaleKnown)
            {
                baseScale = transform.localScale;
                baseScaleKnown = true;
            }
        }

        private void KillTween()
        {
            tween?.Kill();
            tween = null;
        }

        public void OnSpawned()
        {
        }

        public void OnDespawned()
        {
            KillTween();
            if (baseScaleKnown)
            {
                transform.localScale = baseScale;
            }
            ResetJars();
        }

        private void OnDisable()
        {
            KillTween();
        }
    }
}
