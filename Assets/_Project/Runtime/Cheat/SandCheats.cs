#if !DISABLE_CHEATS
using GameKit.App;
using GameKit.Core.Foundation.Logging;
using GameKit.Gameplay.Level;

namespace GameKit.Cheat
{
    /// <summary>Level cheats in the <see cref="CheatPanel"/> (same #if as <see cref="GameCheats"/>).</summary>
    public sealed class SandCheats
    {
        private readonly GameServices services;
        private readonly LevelController level;

        public SandCheats(GameServices services, LevelController level)
        {
            this.services = services;
            this.level = level;
        }

        public void Register(CheatMenu menu)
        {
            menu.Number("Level", "Go To Level", 1, 999, () => level.LevelIndex + 1, n => level.StartLevel(n - 1));
            menu.Button("Level", "Previous Level", () => level.StartLevel(level.LevelIndex - 1));
            menu.Button("Level", "Next Level", () => level.StartLevel(level.LevelIndex + 1));
            menu.Button("Level", "Restart", level.Restart);
            menu.Button("Level", "Win Now", () => level.DebugEndLevel(true), closePanel: true);
            menu.Button("Level", "Lose Now", () => level.DebugEndLevel(false), closePanel: true);
            menu.Button("Level", "Open Locked Tray", level.UnlockTray);
            menu.Button("Level", "Reset Progress to Level 1", ResetProgress);
        }

        private void ResetProgress()
        {
            services.Save.Save(LevelProgressData.Key, new LevelProgressData());
            services.Save.Flush();
            Log.Info("Cheat", "Level progress reset.");
        }
    }
}
#endif
