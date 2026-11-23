#if !DISABLE_CHEATS
using Cysharp.Threading.Tasks;
using GameKit.App;
using GameKit.Core.Foundation.Logging;
using GameKit.UI.Framework;
using UnityEngine;

namespace GameKit.Cheat
{
    /// <summary>
    /// Base cheats in the <see cref="CheatPanel"/> (Editor toolbar "Cheats"; on a device: pause, then tap "Paused" 3 times).
    /// Created at runtime, never placed in a scene, and every cheat file is compiled out with DISABLE_CHEATS
    /// (Tools/GameKit/Cheats), so release builds contain none of it.
    /// Feature cheats: add another class like <see cref="SandCheats"/> in Runtime/Cheat, same #if, with a
    /// <c>Register(CheatMenu)</c> method, and call it from <see cref="Install"/>.
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
            // Development-only lookup, once at startup.
            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap == null || bootstrap.Services == null)
            {
                Log.Warning("Cheat", "No initialised GameBootstrap in the scene; cheats not installed.");
                return;
            }

            var menu = new CheatMenu();
            var level = Object.FindObjectOfType<GameKit.Gameplay.Level.LevelController>();
            if (level != null)
            {
                new SandCheats(bootstrap.Services, level).Register(menu);
            }
            new GameCheats(bootstrap.Services).Register(menu);
            CheatMenu.Current = menu;
            CheatPanel.Create(menu);
            AddPauseTrigger(bootstrap.Services, level);
        }

        /// <summary>On a device: pause, then tap the "Paused" title 3 times to open the panel.</summary>
        private static void AddPauseTrigger(GameServices services, GameKit.Gameplay.Level.LevelController level)
        {
            ConfirmPopup popup = services.Popups != null ? services.Popups.Get<ConfirmPopup>() : null;
            if (popup == null || popup.TitleText == null || level == null)
            {
                Log.Warning("Cheat", "No pause popup title found; the cheat panel opens only from the Editor toolbar.");
                return;
            }
            popup.TitleText.raycastTarget = true; // Taps on the title must reach the trigger.
            var trigger = popup.TitleText.gameObject.AddComponent<CheatTapTrigger>();
            trigger.Condition = () => level != null && level.IsPaused;
        }

        private void Register(CheatMenu menu)
        {
            menu.Stepper("Game", "Time Scale", 0f, 4f, 0.25f, () => Time.timeScale, v => Time.timeScale = v);
            menu.Toggle("Game", "60 FPS (off = 30)", () => Application.targetFrameRate >= 60,
                v => Application.targetFrameRate = v ? 60 : 30);

            menu.Button("Save", "Write Save Now", WriteSave);
            menu.Button("Save", "Delete Save (restart after)", DeleteSave);

            menu.Stepper("Audio", "Music Volume", 0f, 1f, 0.1f,
                () => services.Audio != null ? services.Audio.MusicVolume : 0f,
                v => { if (services.Audio != null) services.Audio.MusicVolume = v; });
            menu.Stepper("Audio", "SFX Volume", 0f, 1f, 0.1f,
                () => services.Audio != null ? services.Audio.SfxVolume : 0f,
                v => { if (services.Audio != null) services.Audio.SfxVolume = v; });

            menu.Button("UI", "Close All Popups", () => services.Popups?.CloseAll());
            menu.Button("UI", "Test Confirm Popup", TestConfirmPopup, closePanel: true);
        }

        private void WriteSave()
        {
            services.Save.Flush();
            Log.Info("Cheat", "Save written.");
        }

        private void DeleteSave()
        {
            services.Save.DeleteAll();
            services.Save.Flush();
            Log.Warning("Cheat", "Save deleted. Restart the game to start from a fresh save.");
        }

        private void TestConfirmPopup()
        {
            var popup = services.Popups != null ? services.Popups.Get<ConfirmPopup>() : null;
            if (popup == null)
            {
                Log.Warning("Cheat", "No ConfirmPopup under PopupManager.");
                return;
            }

            popup.AskAsync("Cheat test: does the popup work?", "GameKit", "Yes", "No")
                .ContinueWith(answer => Log.Info("Cheat", "ConfirmPopup answered: " + answer))
                .Forget();
        }
    }
}
#endif
