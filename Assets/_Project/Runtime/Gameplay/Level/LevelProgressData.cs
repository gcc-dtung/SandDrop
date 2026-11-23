using System;

namespace GameKit.Gameplay.Level
{
    /// <summary>Saved level progress. New fields need a default value here so older saves still load.</summary>
    [Serializable]
    public class LevelProgressData
    {
        public const string Key = "level_progress";

        /// <summary>0-based index of the level to play next (wraps around the level list).</summary>
        public int levelIndex;
    }
}
