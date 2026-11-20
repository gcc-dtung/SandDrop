using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GameKit.UI.Framework
{
    /// <summary>
    /// Base class for every popup. A popup root has its own Canvas + GraphicRaycaster + CanvasGroup
    /// and lives (inactive) under <see cref="PopupManager"/>.
    /// A concrete popup adds its own method that takes its data and then calls <see cref="Open"/>:
    /// <code>
    /// public void Show(int reward) { rewardText.text = reward.ToString(); Open(); }
    /// </code>
    /// Override <see cref="OnOpened"/> / <see cref="OnClosed"/> for work after the animation.
    /// Set a duration to 0 for no animation.
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup))]
    public abstract class PopupView : MonoBehaviour
    {
        [Tooltip("Panel scaled in / out. Empty = the popup root.")]
        [SerializeField] private RectTransform content;
        [Tooltip("Optional: closes the popup when clicked.")]
        [SerializeField] private Button closeButton;
        [Tooltip("Optional: full-screen dim behind the panel; clicking it closes the popup.")]
        [SerializeField] private Button backdropButton;
        [SerializeField] private bool closeOnBackKey = true;
        [SerializeField, Min(0f)] private float openDuration = 0.25f;
        [SerializeField, Min(0f)] private float closeDuration = 0.15f;

        [SerializeField, HideInInspector] private Canvas canvas;
        [SerializeField, HideInInspector] private CanvasGroup canvasGroup;

        private PopupManager manager;
        private Tween tween;
        private bool buttonsWired;
        private UniTaskCompletionSource closedSource;

        public bool IsOpen { get; private set; }

        /// <summary>Back key (Android) / Escape closes this popup when it is on top.</summary>
        public virtual bool CanCloseWithBack => closeOnBackKey;

        public event Action<PopupView> Opened;
        public event Action<PopupView> Closed;

        internal Canvas Canvas
        {
            get
            {
                EnsureReferences();
                return canvas;
            }
        }

        private RectTransform AnimatedTarget => content != null ? content : (RectTransform)transform;

        internal void Bind(PopupManager popupManager)
        {
            manager = popupManager;
        }

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            EnsureReferences();
            WireButtons();
            IsOpen = true;
            KillTween();

            gameObject.SetActive(true);
            manager?.NotifyOpened(this);

            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = false;

            if (openDuration <= 0f)
            {
                canvasGroup.alpha = 1f;
                AnimatedTarget.localScale = Vector3.one;
                FinishOpen();
                return;
            }

            canvasGroup.alpha = 0f;
            AnimatedTarget.localScale = Vector3.one * 0.9f;

            Tween t = null;
            t = DOTween.Sequence()
                .Join(canvasGroup.DOFade(1f, openDuration))
                .Join(AnimatedTarget.DOScale(1f, openDuration).SetEase(Ease.OutBack))
                .SetUpdate(true) // popups must animate while the game is paused
                .SetLink(gameObject)
                .OnComplete(FinishOpen)
                .OnKill(() => { if (tween == t) tween = null; });
            tween = t;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            EnsureReferences();
            IsOpen = false;
            KillTween();
            canvasGroup.interactable = false;

            if (closeDuration <= 0f || !gameObject.activeInHierarchy)
            {
                FinishClose();
                return;
            }

            Tween t = null;
            t = DOTween.Sequence()
                .Join(canvasGroup.DOFade(0f, closeDuration))
                .Join(AnimatedTarget.DOScale(0.9f, closeDuration).SetEase(Ease.InQuad))
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(FinishClose)
                .OnKill(() => { if (tween == t) tween = null; });
            tween = t;
        }

        /// <summary>Completes when the popup is closed (immediately if it is not open).</summary>
        public UniTask WaitUntilClosed(CancellationToken token = default)
        {
            if (!IsOpen)
            {
                return UniTask.CompletedTask;
            }

            closedSource ??= new UniTaskCompletionSource();
            return closedSource.Task.AttachExternalCancellation(token);
        }

        protected virtual void OnOpened()
        {
        }

        protected virtual void OnClosed()
        {
        }

        private void FinishOpen()
        {
            canvasGroup.interactable = true;
            OnOpened();
            Opened?.Invoke(this);
            EventBus<PopupOpened>.Raise(new PopupOpened { PopupType = GetType() });
        }

        private void FinishClose()
        {
            gameObject.SetActive(false);
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = false;
            AnimatedTarget.localScale = Vector3.one;
            manager?.NotifyClosed(this);

            OnClosed();
            Closed?.Invoke(this);
            EventBus<PopupClosed>.Raise(new PopupClosed { PopupType = GetType() });

            var source = closedSource;
            closedSource = null;
            source?.TrySetResult();
        }

        private void WireButtons()
        {
            if (buttonsWired)
            {
                return;
            }
            buttonsWired = true;

            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (backdropButton != null) backdropButton.onClick.AddListener(Close);
        }

        private void KillTween()
        {
            tween?.Kill();
        }

        protected virtual void OnDisable()
        {
            KillTween();
        }

        // References are filled in the Editor (OnValidate); this only covers objects built from code.
        private void EnsureReferences()
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            EnsureReferences();
        }
#endif
    }
}
