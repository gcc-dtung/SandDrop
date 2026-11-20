using GameKit.Core.Contracts;
using UnityEngine;

namespace GameKit.Core.Foundation.Save
{
    /// <summary>
    /// Writes the save to disk when the app is paused (mobile goes to background), on quit,
    /// and every <c>autoSaveInterval</c> seconds when something changed. Initialised by GameBootstrap.
    /// </summary>
    public sealed class SaveRunner : MonoBehaviour
    {
        private ISaveService save;

        public void Init(ISaveService saveService, float autoSaveInterval)
        {
            save = saveService;

            CancelInvoke(nameof(FlushIfDirty));
            if (autoSaveInterval > 0f)
            {
                // InvokeRepeating uses scaled time; realtime is not needed for a periodic safety save.
                InvokeRepeating(nameof(FlushIfDirty), autoSaveInterval, autoSaveInterval);
            }
        }

        private void FlushIfDirty()
        {
            if (save != null && save.IsDirty)
            {
                save.Flush();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                FlushIfDirty();
            }
        }

        private void OnApplicationQuit()
        {
            FlushIfDirty();
        }
    }
}
