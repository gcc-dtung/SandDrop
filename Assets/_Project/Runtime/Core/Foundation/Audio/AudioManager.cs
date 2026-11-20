using DG.Tweening;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Services;
using UnityEngine;

namespace GameKit.Core.Foundation.Audio
{
    /// <summary>
    /// Music (one source, cross-fade on change) and SFX (round-robin over pre-placed sources).
    /// Placed in the Main scene under [Managers]; GameBootstrap calls <see cref="Init"/> with the save service.
    /// Use the "Create Audio Sources" context menu to (re)build the child AudioSources.
    /// </summary>
    public sealed class AudioManager : Singleton<AudioManager>
    {
        private const int DefaultSfxSourceCount = 8;

        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource[] sfxSources = new AudioSource[0];

        private ISaveService save;
        private AudioSettingsData settings = new AudioSettingsData();
        private int nextSfx;
        private Tween musicTween;

        public float MusicVolume
        {
            get => settings.musicVolume;
            set { settings.musicVolume = Mathf.Clamp01(value); ApplyAndSave(); }
        }

        public float SfxVolume
        {
            get => settings.sfxVolume;
            set { settings.sfxVolume = Mathf.Clamp01(value); ApplyAndSave(); }
        }

        public bool MusicMuted
        {
            get => settings.musicMuted;
            set { settings.musicMuted = value; ApplyAndSave(); }
        }

        public bool SfxMuted
        {
            get => settings.sfxMuted;
            set { settings.sfxMuted = value; ApplyAndSave(); }
        }

        private float MusicTargetVolume => settings.musicMuted ? 0f : settings.musicVolume;
        private float SfxTargetVolume => settings.sfxMuted ? 0f : settings.sfxVolume;

        public void Init(ISaveService saveService)
        {
            save = saveService;
            settings = save?.Load<AudioSettingsData>(AudioSettingsData.Key) ?? new AudioSettingsData();
            ApplyVolumes();
        }

        /// <param name="volumeScale">Multiplied with the SFX volume setting.</param>
        public void PlaySfx(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
        {
            if (clip == null || settings.sfxMuted || sfxSources.Length == 0)
            {
                return;
            }

            // Round-robin: the oldest source is reused when all are busy.
            var source = sfxSources[nextSfx];
            nextSfx = (nextSfx + 1) % sfxSources.Length;

            source.pitch = pitch;
            source.volume = SfxTargetVolume * volumeScale;
            source.clip = clip;
            source.Play();
        }

        /// <summary>Switch music with a fade out / fade in. Same clip already playing = nothing happens.</summary>
        public void PlayMusic(AudioClip clip, float fadeSeconds = 0.5f, bool loop = true)
        {
            if (musicSource == null || clip == null)
            {
                return;
            }
            if (musicSource.clip == clip && musicSource.isPlaying)
            {
                return;
            }

            musicTween?.Kill();

            var half = Mathf.Max(0f, fadeSeconds) * 0.5f;
            var sequence = DOTween.Sequence();
            if (musicSource.isPlaying)
            {
                sequence.Append(musicSource.DOFade(0f, half));
            }

            Tween t = null;
            t = sequence
                .AppendCallback(() =>
                {
                    musicSource.clip = clip;
                    musicSource.loop = loop;
                    musicSource.volume = 0f;
                    musicSource.Play();
                })
                .Append(musicSource.DOFade(MusicTargetVolume, half))
                .SetUpdate(true) // keeps fading while the game is paused (timeScale = 0)
                .SetLink(gameObject)
                .OnKill(() => { if (musicTween == t) musicTween = null; });
            musicTween = t;
        }

        public void StopMusic(float fadeSeconds = 0.5f)
        {
            if (musicSource == null || !musicSource.isPlaying)
            {
                return;
            }

            musicTween?.Kill();
            Tween t = null;
            t = musicSource.DOFade(0f, Mathf.Max(0f, fadeSeconds))
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() => musicSource.Stop())
                .OnKill(() => { if (musicTween == t) musicTween = null; });
            musicTween = t;
        }

        private void ApplyAndSave()
        {
            ApplyVolumes();
            save?.Save(AudioSettingsData.Key, settings);
        }

        private void ApplyVolumes()
        {
            if (musicSource != null && musicTween == null)
            {
                musicSource.volume = MusicTargetVolume;
            }
            // SFX volume is applied per PlaySfx; stop SFX already playing when muted.
            if (settings.sfxMuted)
            {
                foreach (var source in sfxSources)
                {
                    if (source != null) source.Stop();
                }
            }
        }

        private void OnDisable()
        {
            musicTween?.Kill();
        }

#if UNITY_EDITOR
        [ContextMenu("Create Audio Sources")]
        private void CreateAudioSources()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                UnityEditor.Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);
            }

            musicSource = CreateSource("Music", loop: true);
            sfxSources = new AudioSource[DefaultSfxSourceCount];
            for (int i = 0; i < sfxSources.Length; i++)
            {
                sfxSources[i] = CreateSource("Sfx_" + i, loop: false);
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private AudioSource CreateSource(string sourceName, bool loop)
        {
            var go = new GameObject(sourceName);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Create Audio Sources");
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }
#endif
    }
}
