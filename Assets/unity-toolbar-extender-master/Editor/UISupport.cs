public class UISupport
{
    // Every scene in this folder gets a button on the right side of the toolbar.
    public const string ScenesFolder = "Assets/_Project/Scenes";

    public struct ToolShortcut
    {
        public string Label;
        public string MenuPath;

        public ToolShortcut(string label, string menuPath)
        {
            Label = label;
            MenuPath = menuPath;
        }
    }

    // Buttons on the left side of the toolbar; each one runs a Unity menu item.
    public static readonly ToolShortcut[] Tools =
    {
        new ToolShortcut("Addressables", "Window/Asset Management/Addressables/Groups"),
        new ToolShortcut("PlayerPrefs", "Tools/BG Tools/PlayerPrefs Editor"),
        new ToolShortcut("Event Bus", "Tools/GameKit/Event Bus Monitor"),
        new ToolShortcut("Cheats", "Tools/GameKit/Cheats/Cheat Window"),
    };
}
