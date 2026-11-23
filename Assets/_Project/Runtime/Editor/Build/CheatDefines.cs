using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GameKit.EditorTools.Build
{
    /// <summary>
    /// Turns the DISABLE_CHEATS define on / off for Android, iOS and Standalone.
    /// Release builds: disable cheats here; that compiles out the whole cheat panel (Runtime/Cheat).
    /// </summary>
    public static class CheatDefines
    {
        private const string Define = "DISABLE_CHEATS";
        private const string EnableMenu = "Tools/GameKit/Cheats/Enable Cheats";
        private const string DisableMenu = "Tools/GameKit/Cheats/Disable Cheats (release)";

        private static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS,
            NamedBuildTarget.Standalone,
        };

        public static bool CheatsEnabled => !GetDefines(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup)).Contains(Define);

        [MenuItem(EnableMenu)]
        private static void Enable() => SetDisabled(false);

        [MenuItem(EnableMenu, true)]
        private static bool CanEnable() => !CheatsEnabled;

        [MenuItem(DisableMenu)]
        private static void Disable() => SetDisabled(true);

        [MenuItem(DisableMenu, true)]
        private static bool CanDisable() => CheatsEnabled;

        private static void SetDisabled(bool disabled)
        {
            foreach (var target in Targets)
            {
                var defines = GetDefines(target);
                if (disabled) defines.Add(Define);
                else defines.Remove(Define);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            }
            Debug.Log($"[Cheats] Cheats {(disabled ? "disabled" : "enabled")} for Android, iOS and Standalone. Scripts will recompile.");
        }

        private static HashSet<string> GetDefines(NamedBuildTarget target)
        {
            var raw = PlayerSettings.GetScriptingDefineSymbols(target);
            return new HashSet<string>(raw.Split(';').Select(d => d.Trim()).Where(d => d.Length > 0));
        }
    }
}
