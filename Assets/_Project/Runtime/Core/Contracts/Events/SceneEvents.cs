namespace GameKit.Core.Contracts
{
    /// <summary>Raised by SceneLoader before a scene starts loading (show a loading screen here).</summary>
    public struct SceneLoadStarted : IEvent
    {
        public string SceneName;
    }

    /// <summary>Raised by SceneLoader once a scene has finished loading.</summary>
    public struct SceneLoaded : IEvent
    {
        public string SceneName;
    }
}
