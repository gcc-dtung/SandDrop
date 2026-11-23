using GameKit.Gameplay.Level;
using UnityEngine;

namespace GameKit.App
{
    /// <summary>
    /// Root config asset (Data/Config/GameConfig). Per-feature configs are added here as fields and passed
    /// to their feature through Init by GameBootstrap; nothing loads config through Resources.
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "GameKit/Game Config")]
    public class GameConfig : ScriptableObject
    {
        [Header("Runtime")]
        [Tooltip("Android / iOS default to 30 FPS; 60 feels smooth on most phones.")]
        [Min(15)] public int targetFrameRate = 60;

        [Header("Levels")]
        public LevelDatabase levels;

        [Header("Save")]
        [Tooltip("Seconds between automatic saves when something changed; 0 = only on pause / quit.")]
        [Min(0)] public float autoSaveInterval = 30f;

        [Header("UI")]
        [Tooltip("Click sound for every ButtonPress without its own clip. Empty = silent.")]
        public AudioClip defaultClickSound;

        [Header("DOTween")]
        [Tooltip("Pre-allocated tween capacity, so DOTween does not grow (and hitch) in the middle of a level.")]
        [Min(1)] public int tweenCapacity = 500;
        [Min(1)] public int sequenceCapacity = 150;
    }
}
