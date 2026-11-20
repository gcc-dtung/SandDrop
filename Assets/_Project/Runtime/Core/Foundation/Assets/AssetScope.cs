using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace GameKit.Core.Foundation.Assets
{
    /// <summary>
    /// Group of loads released together. Typical use: one scope per popup / screen / level.
    /// <code>
    /// scope = assets.CreateScope();
    /// var icon = await scope.LoadAsync&lt;Sprite&gt;("Icons/Coin", token);
    /// ...
    /// scope.Dispose(); // releases everything loaded through this scope
    /// </code>
    /// </summary>
    public sealed class AssetScope : IDisposable
    {
        private readonly AssetService service;
        private readonly List<(object key, Type type)> loaded = new List<(object, Type)>();
        private bool disposed;

        internal AssetScope(AssetService service)
        {
            this.service = service;
        }

        public UniTask<T> LoadAsync<T>(AssetReference reference, CancellationToken token = default) where T : Object
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                throw new ArgumentException("AssetReference is empty or invalid.", nameof(reference));
            }
            return LoadAsync<T>(reference.RuntimeKey, token);
        }

        public async UniTask<T> LoadAsync<T>(object key, CancellationToken token = default) where T : Object
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AssetScope));
            }

            var asset = await service.LoadAsync<T>(key, token);

            if (disposed)
            {
                // Scope was disposed while loading: give the asset straight back.
                service.Release(key, typeof(T));
                throw new OperationCanceledException("AssetScope was disposed while loading.");
            }

            loaded.Add((key, typeof(T)));
            return asset;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            foreach (var (key, type) in loaded)
            {
                service.Release(key, type);
            }
            loaded.Clear();
        }
    }
}
