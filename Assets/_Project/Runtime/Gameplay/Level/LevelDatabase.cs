using System.Collections.Generic;
using UnityEngine;

namespace GameKit.Gameplay.Level
{
    [CreateAssetMenu(menuName = "GameKit/Sand/Level Database", fileName = "LevelDatabase")]
    public sealed class LevelDatabase : ScriptableObject
    {
        public List<SandLevelData> levels = new List<SandLevelData>();

        public int Count => levels.Count;

        /// <summary>Level by 0-based index; wraps around after the last level.</summary>
        public SandLevelData Get(int index)
        {
            if (levels.Count == 0)
            {
                return null;
            }
            int i = ((index % levels.Count) + levels.Count) % levels.Count;
            return levels[i];
        }
    }
}
