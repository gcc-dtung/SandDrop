using DG.Tweening;
using GameKit.Core.Foundation.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameKit.UI.Framework
{
    /// <summary>
    /// Press feedback for a button: scales down while held, back on release, and plays a click sound on click.
    /// Uses <see cref="DefaultClickSound"/> (set by GameBootstrap from GameConfig) when no clip is assigned.
    /// Add to every Button with Tools/GameKit/UI/Add ButtonPress To Buttons; mark exceptions with <see cref="ButtonPressIgnore"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Selectable))]
    public sealed class ButtonPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>Click sound used by every ButtonPress that has no clip of its own.</summary>
        public static AudioClip DefaultClickSound { get; set; }

        [SerializeField, Range(0.5f, 1f)] private float pressedScale = 0.92f;
        [SerializeField, Min(0f)] private float duration = 0.08f;
        [Tooltip("Empty = DefaultClickSound.")]
        [SerializeField] private AudioClip clickSound;
        [SerializeField] private bool playSound = true;

        [SerializeField, HideInInspector] private Selectable selectable;

        private Vector3 baseScale = Vector3.one;
        private bool pressed;
        private Tween tween;

        private void Awake()
        {
            if (selectable == null) selectable = GetComponent<Selectable>();
            baseScale = transform.localScale;

            if (selectable is Button button)
            {
                button.onClick.AddListener(PlayClick);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (selectable != null && !selectable.IsInteractable())
            {
                return;
            }
            pressed = true;
            ScaleTo(baseScale * pressedScale);
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        public void OnPointerExit(PointerEventData eventData) => Release();

        private void Release()
        {
            if (!pressed)
            {
                return;
            }
            pressed = false;
            ScaleTo(baseScale);
        }

        private void PlayClick()
        {
            if (!playSound || !AudioManager.HasInstance)
            {
                return;
            }
            var clip = clickSound != null ? clickSound : DefaultClickSound;
            AudioManager.Instance.PlaySfx(clip);
        }

        private void ScaleTo(Vector3 target)
        {
            tween?.Kill();
            Tween t = null;
            t = transform.DOScale(target, duration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnKill(() => { if (tween == t) tween = null; });
            tween = t;
        }

        private void OnDisable()
        {
            // A button hidden while held must not come back shrunk.
            tween?.Kill();
            pressed = false;
            transform.localScale = baseScale;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (selectable == null) selectable = GetComponent<Selectable>();
        }
#endif
    }
}
