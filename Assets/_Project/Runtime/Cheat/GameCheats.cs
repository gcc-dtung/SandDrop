#if !DISABLE_CHEATS && !DISABLE_SRDEBUGGER
using System.ComponentModel;
using Cysharp.Threading.Tasks;
using GameKit.App;
using GameKit.Core.Foundation.Logging;
using GameKit.UI.Framework;
using SRDebugger;
using UnityEngine;

namespace GameKit.Cheat
{
    /// <summary>
    /// Base cheats shown in the SRDebugger "Options" tab (open it with a triple tap in the top-left corner).
    /// Created at runtime, never placed in a scene, and the whole file is compiled out with DISABLE_CHEATS
    /// (Tools/GameKit/Cheats) or when SRDebugger is disabled, so release builds contain none of it.
    /// Feature cheats: add another class like this one in Runtime/Cheat, same #if, and register it in <see cref="Install"/>.
    /// </summary>
    public sealed class GameCheats
    {
        private readonly GameServices services;

        private GameCheats(GameServices services)
        {
            this.services = services;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Settings.Instance.IsEnabled)
            {
                return;
            }

            // Development-only lookup, once at startup.
            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap == null || bootstrap.Services == null)
            {
                Log.Warning("Cheat", "No initialised GameBootstrap in the scene; cheats not installed.");
                return;
            }

            SRDebug.Instance.AddOptionContainer(new GameCheats(bootstrap.Services));

            var level = Object.FindObjectOfType<GameKit.Gameplay.Level.LevelController>();
            if (level != null)
            {
                SRDebug.Instance.AddOptionContainer(new SandCheats(bootstrap.Services, level));
            }
        }

        // ---- Game ----

        [Category("Game"), DisplayName("Time Scale"), NumberRange(0, 4), Increment(0.25), Sort(0)]
        public float TimeScale
        {
            get => Time.timeScale;
            set => Time.timeScale = value;
        }

        [Category("Game"), DisplayName("60 FPS (off = 30)"), Sort(1)]
        public bool SixtyFps
        {
            get => Application.targetFrameRate >= 60;
            set => Application.targetFrameRate = value ? 60 : 30;
        }

        // ---- Save ----

        [Category("Save"), DisplayName("Write Save Now"), Sort(0)]
        public void WriteSave()
        {
            services.Save.Flush();
            Log.Info("Cheat", "Save written.");
        }

        [Category("Save"), DisplayName("Delete Save (restart after)"), Sort(1)]
        public void DeleteSave()
        {
            services.Save.DeleteAll();
            services.Save.Flush();
            Log.Warning("Cheat", "Save deleted. Restart the game to start from a fresh save.");
        }

        // ---- Audio ----

        [Category("Audio"), DisplayName("Music Volume"), NumberRange(0, 1), Increment(0.1), Sort(0)]
        public float MusicVolume
        {
            get => services.Audio != null ? services.Audio.MusicVolume : 0f;
            set { if (services.Audio != null) services.Audio.MusicVolume = value; }
        }

        [Category("Audio"), DisplayName("SFX Volume"), NumberRange(0, 1), Increment(0.1), Sort(1)]
        public float SfxVolume
        {
            get => services.Audio != null ? services.Audio.SfxVolume : 0f;
            set { if (services.Audio != null) services.Audio.SfxVolume = value; }
        }

        // ---- UI ----

        [Category("UI"), DisplayName("Close All Popups"), Sort(0)]
        public void CloseAllPopups()
        {
            services.Popups?.CloseAll();
        }

        [Category("UI"), DisplayName("Test Confirm Popup"), Sort(1)]
        public void TestConfirmPopup()
        {
            var popup = services.Popups != null ? services.Popups.Get<ConfirmPopup>() : null;
            if (popup == null)
            {
                Log.Warning("Cheat", "No ConfirmPopup under PopupManager.");
                return;
            }

            SRDebug.Instance.HideDebugPanel();
            popup.AskAsync("Cheat test: does the popup work?", "GameKit", "Yes", "No")
                .ContinueWith(answer => Log.Info("Cheat", "ConfirmPopup answered: " + answer))
                .Forget();
        }
    }
}
#endif
