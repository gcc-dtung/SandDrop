using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameKit.Core.Foundation.Assets;
using UnityEngine;
using GameKit.Core.Foundation.Logging;

namespace GameKit.UI.Framework
{
    /// <summary>
    /// Owns every popup: the instances placed (inactive) under this object, plus popups loaded from Addressables.
    /// Keeps the open popups in a stack and gives each one a sorting order above the previous one,
    /// so the newest popup is always drawn and clicked on top. Back key / Escape closes the top popup.
    /// Lives at Canvas_Popup/Popups; GameBootstrap calls <see cref="Init"/>.
    /// </summary>
    public sealed class PopupManager : MonoBehaviour
    {
        [Tooltip("Popup instances under this object. Filled automatically in the Editor.")]
        [SerializeField] private List<PopupView> popups = new List<PopupView>();
        [Tooltip("Sorting order of the first open popup; each popup above it adds Sorting Step.")]
        [SerializeField] private int baseSortingOrder = 20;
        [SerializeField, Min(1)] private int sortingStep = 10;
        [SerializeField] private bool handleBackKey = true;

        private readonly List<PopupView> stack = new List<PopupView>();
        private readonly Dictionary<object, PopupView> loaded = new Dictionary<object, PopupView>();
        private AssetService assets;

        public int OpenCount => stack.Count;
        public PopupView Top => stack.Count > 0 ? stack[stack.Count - 1] : null;

        public void Init(AssetService assetService)
        {
            assets = assetService;
            BindAll();
        }

        private void Awake()
        {
            BindAll();
        }

        /// <summary>Popup of type <typeparamref name="T"/> placed in the scene or already loaded; null if none.</summary>
        public T Get<T>() where T : PopupView
        {
            foreach (var popup in popups)
            {
                if (popup is T match) return match;
            }
            foreach (var popup in loaded.Values)
            {
                if (popup is T match) return match;
            }
            return null;
        }

        /// <summary>Open a popup that takes no data. Popups with data: <c>Get&lt;T&gt;().Show(data)</c>.</summary>
        public T Open<T>() where T : PopupView
        {
            var popup = Get<T>();
            if (popup == null)
            {
                Log.Error("Popups", $"No {typeof(T).Name} under {name}. Add an instance or load it with GetOrLoadAsync.", this);
                return null;
            }
            popup.Open();
            return popup;
        }

        /// <summary>
        /// Instantiate a popup prefab from Addressables (once; later calls return the same instance).
        /// The prefab stays loaded until <see cref="Unload"/>, because the instance uses its assets.
        /// </summary>
        public async UniTask<T> GetOrLoadAsync<T>(object key, CancellationToken token = default) where T : PopupView
        {
            if (loaded.TryGetValue(key, out var existing))
            {
                return (T)existing;
            }
            if (assets == null)
            {
                throw new InvalidOperationException("[Popups] PopupManager.Init(assets) was not called.");
            }

            var prefab = await assets.LoadAsync<GameObject>(key, token);
            if (loaded.TryGetValue(key, out existing))
            {
                // Loaded twice in parallel: keep the first instance.
                assets.Release<GameObject>(key);
                return (T)existing;
            }

            var go = Instantiate(prefab, transform);
            go.SetActive(false);
            if (!go.TryGetComponent<T>(out var view))
            {
                Destroy(go);
                assets.Release<GameObject>(key);
                throw new InvalidOperationException($"[Popups] Prefab '{key}' has no {typeof(T).Name} on its root.");
            }

            view.Bind(this);
            loaded[key] = view;
            return view;
        }

        /// <summary>Destroy a popup loaded with GetOrLoadAsync and release its prefab.</summary>
        public void Unload(object key)
        {
            if (!loaded.TryGetValue(key, out var view))
            {
                return;
            }

            loaded.Remove(key);
            if (view != null)
            {
                stack.Remove(view);
                Destroy(view.gameObject);
            }
            assets?.Release<GameObject>(key);
            ApplySorting();
        }

        /// <summary>Close the top popup if it allows it. Returns true when a popup was closed.</summary>
        public bool HandleBack()
        {
            var top = Top;
            if (top == null || !top.CanCloseWithBack)
            {
                return false;
            }
            top.Close();
            return true;
        }

        public void CloseAll()
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (i < stack.Count) stack[i].Close();
            }
        }

        internal void NotifyOpened(PopupView popup)
        {
            stack.Remove(popup);
            stack.Add(popup);
            ApplySorting();
        }

        internal void NotifyClosed(PopupView popup)
        {
            if (stack.Remove(popup))
            {
                ApplySorting();
            }
        }

        private void ApplySorting()
        {
            for (int i = 0; i < stack.Count; i++)
            {
                var canvas = stack[i].Canvas;
                canvas.overrideSorting = true;
                canvas.sortingOrder = baseSortingOrder + (i + 1) * sortingStep;
            }
        }

        private void BindAll()
        {
            foreach (var popup in popups)
            {
                if (popup != null) popup.Bind(this);
            }
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        private void Update()
        {
            if (handleBackKey && Input.GetKeyDown(KeyCode.Escape))
            {
                HandleBack();
            }
        }
#endif

        private void OnDestroy()
        {
            if (assets == null)
            {
                return;
            }
            foreach (var key in loaded.Keys)
            {
                assets.Release<GameObject>(key);
            }
            loaded.Clear();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            popups.Clear();
            popups.AddRange(GetComponentsInChildren<PopupView>(true));
        }
#endif
    }
}
