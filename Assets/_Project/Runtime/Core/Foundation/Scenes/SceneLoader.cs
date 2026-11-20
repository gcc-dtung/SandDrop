using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Events;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace GameKit.Core.Foundation.Scenes
{
    /// <summary>
    /// Loads scenes from Build Settings or from Addressables, reporting progress and raising
    /// <see cref="SceneLoadStarted"/> / <see cref="SceneLoaded"/>.
    /// The project keeps one Main scene; use this for additive content (levels, big screens) or a Boot → Main flow.
    /// </summary>
    public sealed class SceneLoader
    {
        /// <summary>Load a scene listed in Build Settings.</summary>
        public async UniTask LoadBuildSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single,
            IProgress<float> progress = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            EventBus<SceneLoadStarted>.Raise(new SceneLoadStarted { SceneName = sceneName });

            var operation = SceneManager.LoadSceneAsync(sceneName, mode);
            if (operation == null)
            {
                throw new ArgumentException($"[Scenes] '{sceneName}' is not in Build Settings.", nameof(sceneName));
            }

            // A scene load cannot be cancelled once started; the token only stops us waiting for it.
            await operation.ToUniTask(progress: progress, cancellationToken: token);
            EventBus<SceneLoaded>.Raise(new SceneLoaded { SceneName = sceneName });
        }

        public UniTask UnloadBuildSceneAsync(string sceneName)
        {
            var operation = SceneManager.UnloadSceneAsync(sceneName);
            return operation == null ? UniTask.CompletedTask : operation.ToUniTask();
        }

        /// <summary>Load an Addressables scene. Keep the returned instance and pass it to <see cref="UnloadAddressableSceneAsync"/>.</summary>
        public async UniTask<SceneInstance> LoadAddressableSceneAsync(object key, LoadSceneMode mode = LoadSceneMode.Additive,
            IProgress<float> progress = null)
        {
            var name = key?.ToString();
            EventBus<SceneLoadStarted>.Raise(new SceneLoadStarted { SceneName = name });

            var instance = await Addressables.LoadSceneAsync(key, mode).ToUniTask(progress: progress);
            EventBus<SceneLoaded>.Raise(new SceneLoaded { SceneName = instance.Scene.name });
            return instance;
        }

        public UniTask UnloadAddressableSceneAsync(SceneInstance instance)
        {
            return Addressables.UnloadSceneAsync(instance).ToUniTask();
        }
    }
}
