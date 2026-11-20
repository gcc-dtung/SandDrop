using System.IO;
using DG.Tweening;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Assets;
using GameKit.Core.Foundation.Audio;
using GameKit.Core.Foundation.Events;
using GameKit.Core.Foundation.Save;
using GameKit.Core.Foundation.Scenes;
using GameKit.Save;
using GameKit.UI.Framework;
using UnityEngine;
using GameKit.Core.Foundation.Logging;

namespace GameKit.App
{
    /// <summary>
    /// Composition root and the only place that creates services. Runs before every other script.
    /// Order: runtime settings → save (+ migrate) → services → Init(...) of each scene component → <see cref="GameBootstrapped"/>.
    /// Scene components are referenced with [SerializeField] here and receive their dependencies through Init(...),
    /// never by reading them in Awake.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private GameConfig config;

        [Header("Infrastructure (in [Managers])")]
        [SerializeField] private SaveRunner saveRunner;
        [SerializeField] private AudioManager audioManager;

        [Header("UI (in ---- GIAO DIỆN ----)")]
        [SerializeField] private PopupManager popupManager;

        public GameServices Services { get; private set; }

        private void Awake()
        {
            if (config == null)
            {
                Log.Error("GameBootstrap", "GameConfig is not assigned.", this);
                enabled = false;
                return;
            }

            ApplyRuntimeSettings(config);

            var save = new JsonFileSaveService(
                Path.Combine(Application.persistentDataPath, "Save"),
                SaveMigrations.CurrentVersion,
                SaveMigrations.All);

            Services = new GameServices(config, save, new AssetService(), new SceneLoader(), audioManager, popupManager);

            InitInfrastructure(Services);
            InitComponents(Services);

            EventBus<GameBootstrapped>.Raise(new GameBootstrapped());
        }

        private void OnDestroy()
        {
            if (Services == null)
            {
                return;
            }

            Services.Save.Flush();
            Services.Assets.ReleaseAll();
        }

        private static void ApplyRuntimeSettings(GameConfig cfg)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD && !GAMEKIT_VERBOSE_LOGS
            // Release: drop plain Debug.Log (ours and third-party); warnings, errors and exceptions still go through.
            Debug.unityLogger.filterLogType = LogType.Warning;
#endif
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = cfg.targetFrameRate;
            DOTween.SetTweensCapacity(cfg.tweenCapacity, cfg.sequenceCapacity);
        }

        private void InitInfrastructure(GameServices services)
        {
            if (saveRunner != null)
            {
                saveRunner.Init(services.Save, services.Config.autoSaveInterval);
            }
            else
            {
                Log.Warning("GameBootstrap", "SaveRunner is not assigned: the save is only written on shutdown.", this);
            }

            if (audioManager != null)
            {
                audioManager.Init(services.Save);
            }
            else
            {
                Log.Warning("GameBootstrap", "AudioManager is not assigned.", this);
            }

            ButtonPress.DefaultClickSound = services.Config.defaultClickSound;

            if (popupManager != null)
            {
                popupManager.Init(services.Assets);
            }
            else
            {
                Log.Warning("GameBootstrap", "PopupManager is not assigned.", this);
            }
        }

        private void InitComponents(GameServices services)
        {
            // Wire scene components here, e.g.:
            // [SerializeField] private LevelController levelController;
            // levelController.Init(services.Config.Level, services.Save);
        }
    }
}
