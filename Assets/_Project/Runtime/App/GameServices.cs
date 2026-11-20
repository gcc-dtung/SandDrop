using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Assets;
using GameKit.Core.Foundation.Audio;
using GameKit.Core.Foundation.Scenes;
using GameKit.UI.Framework;

namespace GameKit.App
{
    /// <summary>
    /// Holds the game-wide services created by <see cref="GameBootstrap"/>.
    /// Add a read-only property per new service and set it in the constructor.
    /// Components receive the services they need through their own Init(...) method; they do not read this class globally.
    /// </summary>
    public sealed class GameServices
    {
        public GameConfig Config { get; }
        public ISaveService Save { get; }
        public AssetService Assets { get; }
        public SceneLoader Scenes { get; }
        public AudioManager Audio { get; }
        public PopupManager Popups { get; }

        public GameServices(GameConfig config, ISaveService save, AssetService assets, SceneLoader scenes, AudioManager audio,
            PopupManager popups)
        {
            Config = config;
            Save = save;
            Assets = assets;
            Scenes = scenes;
            Audio = audio;
            Popups = popups;
        }
    }
}
