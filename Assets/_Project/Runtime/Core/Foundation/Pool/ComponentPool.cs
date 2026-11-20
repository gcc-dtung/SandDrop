using System;
using System.Collections.Generic;
using GameKit.Core.Contracts;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace GameKit.Core.Foundation.Pool
{
    /// <summary>
    /// Pool of instances of a template component (usually an inactive child placed in the scene, or a prefab).
    /// Get activates the instance and calls <see cref="IPoolable.OnSpawned"/>; Release calls
    /// <see cref="IPoolable.OnDespawned"/>, deactivates it and moves it back under the pool parent.
    /// <code>
    /// pool = new ComponentPool&lt;Bubble&gt;(bubbleTemplate, poolRoot);
    /// var bubble = pool.Get();
    /// pool.Release(bubble);
    /// </code>
    /// </summary>
    public sealed class ComponentPool<T> : IDisposable where T : Component
    {
        private readonly T template;
        private readonly Transform parent;
        private readonly ObjectPool<T> pool;
        // IPoolable components are looked up once per instance, at creation, not on every Get / Release.
        private readonly Dictionary<T, IPoolable[]> poolables = new Dictionary<T, IPoolable[]>();

        public int CountActive => pool.CountActive;
        public int CountInactive => pool.CountInactive;

        /// <param name="template">Object to clone. Keep it inactive so its Awake / OnEnable do not run on the template itself.</param>
        /// <param name="parent">Where inactive instances live; null = scene root.</param>
        public ComponentPool(T template, Transform parent = null, int defaultCapacity = 10, int maxSize = 1000)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            this.template = template;
            this.parent = parent;
            pool = new ObjectPool<T>(Create, OnGet, OnRelease, OnDestroyItem,
                collectionCheck: Application.isEditor, defaultCapacity: defaultCapacity, maxSize: maxSize);
        }

        public T Get() => pool.Get();

        public void Release(T item)
        {
            if (item == null)
            {
                return; // destroyed elsewhere (e.g. scene unloaded)
            }
            pool.Release(item);
        }

        /// <summary>Create <paramref name="count"/> inactive instances up front, so the first Gets do not instantiate.</summary>
        public void Prewarm(int count)
        {
            var items = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(pool.Get());
            }
            foreach (var item in items)
            {
                pool.Release(item);
            }
        }

        /// <summary>Destroy inactive instances. Active ones are not tracked by the pool and stay alive.</summary>
        public void Clear() => pool.Clear();

        public void Dispose() => pool.Dispose();

        private T Create()
        {
            var item = Object.Instantiate(template, parent);
            item.gameObject.SetActive(false);
            poolables[item] = item.GetComponentsInChildren<IPoolable>(true);
            return item;
        }

        private void OnGet(T item)
        {
            item.gameObject.SetActive(true);
            if (poolables.TryGetValue(item, out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    callback.OnSpawned();
                }
            }
        }

        private void OnRelease(T item)
        {
            if (poolables.TryGetValue(item, out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    callback.OnDespawned();
                }
            }

            item.gameObject.SetActive(false);
            if (item.transform.parent != parent)
            {
                item.transform.SetParent(parent, false);
            }
        }

        private void OnDestroyItem(T item)
        {
            poolables.Remove(item);

            // Leaving Play mode destroys the scene before the pool is cleared: the item may already be gone.
            if (item == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(item.gameObject);
            }
            else
            {
                Object.DestroyImmediate(item.gameObject);
            }
        }
    }
}
