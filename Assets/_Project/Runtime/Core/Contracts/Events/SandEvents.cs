namespace GameKit.Core.Contracts
{
    /// <summary>A level was built and is ready to play. LevelIndex is 0-based.</summary>
    public struct LevelStarted : IEvent
    {
        public int LevelIndex;
    }

    /// <summary>A tappable block was tapped and collected.</summary>
    public struct BlockCollected : IEvent
    {
        public int Block;
        public int Color;
        /// <summary>True when the sand went to a tray, false when it went to the waiting row.</summary>
        public bool ToTray;
    }

    /// <summary>A block that cannot be tapped yet (something still around it) was tapped.</summary>
    public struct BlockBlocked : IEvent
    {
        public int Block;
    }

    /// <summary>A tray reached 3 and disappeared.</summary>
    public struct TrayCompleted : IEvent
    {
        public int Tray;
        public int Color;
    }

    /// <summary>The locked tray was opened (the "+" / watch-ad tray).</summary>
    public struct TrayUnlocked : IEvent
    {
        public int Tray;
    }

    public struct LevelWon : IEvent
    {
        public int LevelIndex;
    }

    public struct LevelFailed : IEvent
    {
        public int LevelIndex;
    }

    /// <summary>Revive used: the waiting row got extra slots.</summary>
    public struct ReviveUsed : IEvent
    {
        public int LevelIndex;
    }
}
