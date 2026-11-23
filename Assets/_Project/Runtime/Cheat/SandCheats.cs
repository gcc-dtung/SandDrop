#if !DISABLE_CHEATS && !DISABLE_SRDEBUGGER
using System.ComponentModel;
using GameKit.App;
using GameKit.Core.Foundation.Logging;
using GameKit.Gameplay.Level;
using SRDebugger;

namespace GameKit.Cheat
{
    /// <summary>Level cheats in the SRDebugger "Options" tab (same #if as <see cref="GameCheats"/>).</summary>
    public sealed class SandCheats
    {
        private readonly GameServices services;
        private readonly LevelController level;

        public SandCheats(GameServices services, LevelController level)
        {
            this.services = services;
            this.level = level;
        }

        [Category("Level"), DisplayName("Level (1-based)"), NumberRange(1, 999), Sort(0)]
        public int LevelNumber
        {
            get => level.LevelIndex + 1;
            set => level.StartLevel(value - 1);
        }

        [Category("Level"), DisplayName("Previous Level"), Sort(1)]
        public void Previous() => level.StartLevel(level.LevelIndex - 1);

        [Category("Level"), DisplayName("Next Level"), Sort(2)]
        public void Next() => level.StartLevel(level.LevelIndex + 1);

        [Category("Level"), DisplayName("Restart"), Sort(3)]
        public void Restart() => level.Restart();

        [Category("Level"), DisplayName("Win Now"), Sort(4)]
        public void Win()
        {
            SRDebug.Instance.HideDebugPanel();
            level.DebugEndLevel(true);
        }

        [Category("Level"), DisplayName("Lose Now"), Sort(5)]
        public void Lose()
        {
            SRDebug.Instance.HideDebugPanel();
            level.DebugEndLevel(false);
        }

        [Category("Level"), DisplayName("Open Locked Tray"), Sort(6)]
        public void UnlockTray() => level.UnlockTray();

        [Category("Level"), DisplayName("Reset Progress to Level 1"), Sort(7)]
        public void ResetProgress()
        {
            services.Save.Save(LevelProgressData.Key, new LevelProgressData());
            services.Save.Flush();
            Log.Info("Cheat", "Level progress reset.");
        }
    }
}
#endif
