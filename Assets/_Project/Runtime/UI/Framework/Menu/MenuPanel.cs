using DG.Tweening;
using UnityEngine;

namespace GameKit.UI.Framework
{
    /// <summary>
    /// One tab of the main menu (Home, Shop, Settings…). Shown / hidden by <see cref="MenuNavigator"/> with a fade.
    /// Override <see cref="OnShown"/> to refresh its content each time the tab is opened.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class MenuPanel : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float fadeDuration = 0.15f;
        [SerializeField, HideInInspector] private CanvasGroup canvasGroup;

        private Tween tween;

        public bool IsShown { get; private set; }

        public void Show(bool instant = false)
        {
            if (IsShown)
            {
                return;
            }
            SetVisible(true, instant);
            OnShown();
        }

        public void Hide(bool instant = false)
        {
            if (!IsShown)
            {
                return;
            }
            SetVisible(false, instant);
            OnHidden();
        }

        /// <summary>Set the state without animation or callbacks (initial layout).</summary>
        internal void SetStateImmediate(bool shown)
        {
            SetVisible(shown, true);
        }

        protected virtual void OnShown()
        {
        }

        protected virtual void OnHidden()
        {
        }

        private void SetVisible(bool shown, bool instant)
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            IsShown = shown;
            tween?.Kill();
            canvasGroup.interactable = shown;
            canvasGroup.blocksRaycasts = shown;

            if (instant || fadeDuration <= 0f || (!shown && !gameObject.activeInHierarchy))
            {
                canvasGroup.alpha = shown ? 1f : 0f;
                gameObject.SetActive(shown);
                return;
            }

            gameObject.SetActive(true);
            Tween t = null;
            t = canvasGroup.DOFade(shown ? 1f : 0f, fadeDuration)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() => { if (!shown) gameObject.SetActive(false); })
                .OnKill(() => { if (tween == t) tween = null; });
            tween = t;
        }

        private void OnDisable()
        {
            tween?.Kill();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }
#endif
    }
}
