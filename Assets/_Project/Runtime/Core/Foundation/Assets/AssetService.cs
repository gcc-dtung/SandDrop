using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using GameKit.Core.Foundation.Logging;
using Object = UnityEngine.Object;

namespace GameKit.Core.Foundation.Assets
{
    /// <summary>
    /// Loads Addressables with reference counting: loading the same (key, type) twice shares one handle,
    /// and the asset is released only after every load has been matched by a <see cref="Release{T}(object)"/>.
    /// Prefer <see cref="CreateScope"/> for a screen / level / popup: dispose the scope and everything it loaded is released.
    /// </summary>
    public sealed class AssetService
    {
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly object Key;
            public readonly Type Type;

            public CacheKey(object key, Type type)
            {
                Key = key;
                Type = type;
            }

            public bool Equals(CacheKey other) => Equals(Key, other.Key) && Type == other.Type;
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => ((Key?.GetHashCode() ?? 0) * 397) ^ Type.GetHashCode();
            public override string ToString() => $"{Key} ({Type.Name})";
        }

        private sealed class Entry
        {
            public AsyncOperationHandle Handle;
            public int RefCount;
        }

        private readonly Dictionary<CacheKey, Entry> cache = new Dictionary<CacheKey, Entry>();

        /// <summary>Number of distinct (key, type) assets currently held.</summary>
        public int LoadedCount => cache.Count;

        public AssetScope CreateScope() => new AssetScope(this);

        public UniTask<T> LoadAsync<T>(AssetReference reference, CancellationToken token = default) where T : Object
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                throw new ArgumentException("AssetReference is empty or invalid.", nameof(reference));
            }
            return LoadAsync<T>(reference.RuntimeKey, token);
        }

        /// <summary>
        /// Load an asset by address / key. Every successful call must be matched by one Release with the same key and type.
        /// If <paramref name="token"/> is cancelled the load still finishes in the background and is released immediately.
        /// </summary>
        public async UniTask<T> LoadAsync<T>(object key, CancellationToken token = default) where T : Object
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            token.ThrowIfCancellationRequested();

            var cacheKey = new CacheKey(key, typeof(T));
            if (!cache.TryGetValue(cacheKey, out var entry))
            {
                entry = new Entry { Handle = Addressables.LoadAssetAsync<T>(key) };
                cache[cacheKey] = entry;
            }
            entry.RefCount++;

            var handle = entry.Handle;
            if (!handle.IsDone)
            {
                // Not passing the token: the Addressables operation cannot be cancelled, we wait for it and release below.
                try
                {
                    await handle.ToUniTask();
                }
                catch (Exception) when (handle.IsValid() && handle.Status == AsyncOperationStatus.Failed)
                {
                    // Reported with the key just below.
                }
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var error = handle.OperationException;
                ReleaseInternal(cacheKey);
                throw new InvalidOperationException($"[Assets] Failed to load {cacheKey}.", error);
            }

            if (token.IsCancellationRequested)
            {
                ReleaseInternal(cacheKey);
                throw new OperationCanceledException(token);
            }

            return (T)handle.Result;
        }

        public void Release<T>(AssetReference reference) where T : Object
        {
            if (reference != null)
            {
                Release<T>(reference.RuntimeKey);
            }
        }

        public void Release<T>(object key) where T : Object
        {
            Release(key, typeof(T));
        }

        public void Release(object key, Type type)
        {
            if (key == null)
            {
                return;
            }

            if (!ReleaseInternal(new CacheKey(key, type)))
            {
                Log.Warning("Assets", $"Release called for {key} ({type.Name}) which is not loaded.");
            }
        }

        /// <summary>Release everything, whatever the reference counts. For shutdown / full reset only.</summary>
        public void ReleaseAll()
        {
            foreach (var entry in cache.Values)
            {
                if (entry.Handle.IsValid())
                {
                    Addressables.Release(entry.Handle);
                }
            }
            cache.Clear();
        }

        private bool ReleaseInternal(CacheKey cacheKey)
        {
            if (!cache.TryGetValue(cacheKey, out var entry))
            {
                return false;
            }

            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                cache.Remove(cacheKey);
                if (entry.Handle.IsValid())
                {
                    Addressables.Release(entry.Handle);
                }
            }
            return true;
        }
    }
}
