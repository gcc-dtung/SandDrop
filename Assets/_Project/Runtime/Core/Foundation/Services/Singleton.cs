using GameKit.Core.Foundation.Logging;
using UnityEngine;

namespace GameKit.Core.Foundation.Services
{
    /// <summary>
    /// Scene-placed singleton for infrastructure only (AudioManager…), never for game logic.
    /// It does not create itself: put the object in the Main scene. A second instance logs an error and destroys itself.
    /// </summary>
    public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
    {
        public static T Instance { get; private set; }

        public static bool HasInstance => Instance != null;

        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Log.Error(typeof(T).Name, $"A second instance was found on '{name}'; destroying it.", this);
                Destroy(gameObject);
                return;
            }
            Instance = (T)this;
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
