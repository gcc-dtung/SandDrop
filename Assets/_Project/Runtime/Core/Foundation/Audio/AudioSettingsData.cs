using System;

namespace GameKit.Core.Foundation.Audio
{
    /// <summary>Saved audio preferences. New fields need a default value here so older saves still load correctly.</summary>
    [Serializable]
    public class AudioSettingsData
    {
        public const string Key = "audio_settings";

        public float musicVolume = 1f;
        public float sfxVolume = 1f;
        public bool musicMuted;
        public bool sfxMuted;
    }
}
