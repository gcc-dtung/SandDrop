using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Audio;
using GameKit.Core.Foundation.Events;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameKit.Gameplay.Audio
{
    /// <summary>Plays a sound for each gameplay event. Empty clips stay silent (prototype has no audio yet).</summary>
    public sealed class SandAudioEvents : MonoBehaviour
    {
        [SerializeField] private AudioClip tap;
        [SerializeField] private AudioClip blocked;
        [SerializeField] private AudioClip toWaiting;
        [SerializeField] private AudioClip trayComplete;
        [SerializeField] private AudioClip trayUnlock;
        [SerializeField] private AudioClip win;
        [SerializeField] private AudioClip fail;
        [SerializeField] private AudioClip revive;

        private AudioManager audioManager;
        private bool subscribed;

        public void Init(AudioManager audio)
        {
            audioManager = audio;
            if (subscribed)
            {
                return;
            }
            subscribed = true;

            var token = this.GetCancellationTokenOnDestroy();
            EventBus<BlockCollected>.Subscribe(e => Play(e.ToTray ? tap : toWaiting), token);
            EventBus<BlockBlocked>.Subscribe(_ => Play(blocked), token);
            EventBus<TrayCompleted>.Subscribe(_ => Play(trayComplete), token);
            EventBus<TrayUnlocked>.Subscribe(_ => Play(trayUnlock), token);
            EventBus<LevelWon>.Subscribe(_ => Play(win), token);
            EventBus<LevelFailed>.Subscribe(_ => Play(fail), token);
            EventBus<ReviveUsed>.Subscribe(_ => Play(revive), token);
        }

        private void Play(AudioClip clip)
        {
            if (clip != null && audioManager != null)
            {
                audioManager.PlaySfx(clip);
            }
        }
    }
}
